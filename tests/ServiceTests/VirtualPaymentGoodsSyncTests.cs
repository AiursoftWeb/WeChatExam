using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentGoodsSyncTests
{
    private SqliteContext _db = null!;
    private DbContextOptions<SqliteContext> _dbOptions = null!;
    private VipProduct _product = null!;
    private readonly FakeGoodsGateway _gateway = new();
    private IOptions<AppSettings> _options = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _dbOptions = new DbContextOptionsBuilder<SqliteContext>().UseSqlite(
            $"Data Source=goods-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10").Options;
        _db = new SqliteContext(_dbOptions);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _options = Settings();
        _product = new VipProduct { Name = "Course", PriceInFen = 19800, DurationDays = 365,
            TotalCategory = new TotalCategory { Title = "History" } };
        _db.Add(_product); await _db.SaveChangesAsync();
    }

    [TestCleanup] public Task Cleanup() => _db.DisposeAsync().AsTask();

    [TestMethod]
    public async Task DisabledSynchronizerDoesNotContactWeChatOrCreateTasks()
    {
        await Worker(_db, Settings(enabled: false)).RunOnceAsync();
        Assert.AreEqual(0, _gateway.Queries); Assert.AreEqual(0, await _db.VirtualPaymentGoods.CountAsync());
        Assert.AreEqual(0, _gateway.Starts);
    }

    [TestMethod]
    public async Task CreationAndEditsAreDurableWithoutHttpInTheSaveRequest()
    {
        var revision = _product.VirtualGoodsRevision;
        Assert.AreNotEqual(Guid.Empty, revision);
        await _db.SaveChangesAsync(); Assert.AreEqual(revision, _product.VirtualGoodsRevision);
        var service = new VipProductService(_db, _options);
        await service.UpdateAsync(_product.Id, "Course", VipProductType.Category, _product.TotalCategoryId, 19800, 365, true);
        Assert.AreEqual(revision, _product.VirtualGoodsRevision);
        await service.UpdateAsync(_product.Id, "Course", VipProductType.Category, _product.TotalCategoryId, 18800, 365, true);
        Assert.AreNotEqual(revision, _product.VirtualGoodsRevision); Assert.AreEqual(18800, _product.PriceInFen);
        Assert.AreEqual(0, _gateway.Starts);
        await Worker(_db).RunOnceAsync();
        Assert.AreEqual(18800, (await _db.VirtualPaymentGoods.SingleAsync()).PriceInFen);
    }

    [TestMethod]
    public async Task RestartRecoversEachStepAndOnlyVerifiedPublishedVersionCanBeBought()
    {
        await Worker(_db).RunOnceAsync();
        var row = await _db.VirtualPaymentGoods.SingleAsync();
        Assert.AreEqual(20, row.GoodsId.Length); Assert.AreEqual(VirtualGoodsState.Uploading, row.State);
        Assert.AreEqual(1, _gateway.Starts);
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => Catalog().ResolveAsync(_product));
        await Tick(); Assert.AreEqual(VirtualGoodsState.ReadyToPublish, row.State);
        await Tick(); Assert.AreEqual(VirtualGoodsState.Publishing, row.State);
        await Tick(); Assert.AreEqual(VirtualGoodsState.Published, row.State);
        Assert.AreEqual(2, _gateway.Starts);
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => Catalog().ResolveAsync(_product));
        row.PublishedAt = DateTime.UtcNow.AddMinutes(-11); await _db.SaveChangesAsync();
        var mapping = await Catalog().ResolveAsync(_product);
        Assert.AreEqual(19800, mapping.PriceInFen); Assert.AreEqual(row.GoodsId, mapping.ProductId);
        await Tick(); Assert.AreEqual(2, _gateway.Starts); Assert.AreEqual(1, await _db.VirtualPaymentGoods.CountAsync());
        Assert.AreEqual(0, await _db.PaymentOrders.CountAsync()); Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcceptedRequestWithLostReplyIsQueriedAfterRestartAndNeverSubmittedAgain(bool publishing)
    {
        if (publishing) { await Worker(_db).RunOnceAsync(); await Tick(); }
        _gateway.ThrowAfterStart = true;
        if (publishing) await Tick(); else await Worker(_db).RunOnceAsync();
        var row = await _db.VirtualPaymentGoods.SingleAsync();
        Assert.AreEqual(publishing ? VirtualGoodsState.Publishing : VirtualGoodsState.Uploading, row.State);
        Assert.AreEqual("request-failed", row.Error);
        Assert.AreEqual(1, row.Failures); Assert.IsTrue(row.NextAttemptAt > DateTime.UtcNow);
        _gateway.ThrowAfterStart = false;
        await Tick();
        Assert.AreEqual(publishing ? VirtualGoodsState.Published : VirtualGoodsState.ReadyToPublish, row.State);
        Assert.AreEqual(publishing ? 2 : 1, _gateway.Starts);
    }

    [TestMethod]
    [DataRow("id")]
    [DataRow("price")]
    [DataRow("name")]
    [DataRow("status")]
    public async Task ForeignOrMismatchedTaskCannotActivateAnItem(string mismatch)
    {
        await Worker(_db).RunOnceAsync(); var row = await _db.VirtualPaymentGoods.SingleAsync();
        var observed = _gateway.Upload.Items.Single();
        _gateway.Upload = new(3, [observed with
        {
            Id = mismatch == "id" ? "another-item" : observed.Id,
            Price = mismatch == "price" ? 1 : observed.Price,
            Name = mismatch == "name" ? "another-name" : observed.Name,
            Status = mismatch == "status" ? 1 : observed.Status
        }]);
        await Tick();
        Assert.AreNotEqual(VirtualGoodsState.Published, row.State);
        Assert.AreEqual(mismatch == "id" ? "task-unconfirmed" : mismatch == "status" ? "goods-id-conflict" : "platform-rejected", row.Error);
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => Catalog().ResolveAsync(_product));
        Assert.AreEqual(1, _gateway.Starts);
    }

    [TestMethod]
    public async Task PriceChangeDuringUploadPreservesOldGoodsButCannotUseThemForNewPurchases()
    {
        await Worker(_db).RunOnceAsync(); var original = await _db.VirtualPaymentGoods.SingleAsync();
        var id = original.GoodsId;
        _product.PriceInFen = 20800; await _db.SaveChangesAsync();
        await Tick(); await Tick(); await Tick(); // Old version finishes; its result is not current.
        original.PublishedAt = DateTime.UtcNow.AddMinutes(-11); await _db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => Catalog().ResolveAsync(_product));
        Assert.AreEqual(2, await _db.VirtualPaymentGoods.CountAsync());
        Assert.AreEqual(19800, original.PriceInFen); Assert.AreEqual(id, original.GoodsId);
        await Tick();
        var next = await _db.VirtualPaymentGoods.SingleAsync(g => g.PriceInFen == 20800);
        Assert.AreNotEqual(id, next.GoodsId); Assert.AreEqual(VirtualGoodsState.Uploading, next.State);
    }

    [TestMethod]
    public async Task DisableStopsNewPurchasesAndDeleteRetainsSyncReceipts()
    {
        await Publish(); var row = await _db.VirtualPaymentGoods.SingleAsync();
        _product.IsEnabled = false; await _db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => Catalog().ResolveAsync(_product));
        await new VipProductService(_db, _options).DeleteAsync(_product.Id);
        await _db.Entry(row).ReloadAsync();
        Assert.IsNull(row.VipProductId); Assert.AreEqual(_product.Id, row.SourceProductId);
        Assert.AreEqual(VirtualGoodsState.Published, row.State);
    }

    [TestMethod]
    [DataRow(1, "app", "offer")]
    [DataRow(0, "another-app", "offer")]
    [DataRow(0, "app", "another-offer")]
    public async Task PublishedGoodsAreBoundToAppEnvironmentAndOffer(int environment, string app, string offer)
    {
        await Publish();
        var other = new VirtualPaymentGoodsCatalog(_db, Settings(environment: environment, app: app, offer: offer));
        await Assert.ThrowsExactlyAsync<VirtualPaymentCheckoutException>(() => other.ResolveAsync(_product));
    }

    [TestMethod]
    public async Task BusyPlatformAndActiveReplicaLeaseCannotStartAnotherTask()
    {
        _gateway.Upload = new(1, []);
        await Worker(_db).RunOnceAsync(); Assert.AreEqual(0, _gateway.Starts);
        var row = await _db.VirtualPaymentGoods.SingleAsync(); Assert.AreEqual(VirtualGoodsState.Queued, row.State);
        await _db.VirtualPaymentGoodsLeases.ExecuteUpdateAsync(s => s.SetProperty(l => l.Until, DateTime.UtcNow.AddMinutes(5)));
        var queries = _gateway.Queries;
        await using var other = new SqliteContext(_dbOptions);
        await Worker(other).RunOnceAsync(); Assert.AreEqual(queries, _gateway.Queries); Assert.AreEqual(0, _gateway.Starts);
    }

    [TestMethod]
    public async Task SupersededQueuedRevisionIsNotUploaded()
    {
        _gateway.Upload = new(1, []); await Worker(_db).RunOnceAsync();
        var old = await _db.VirtualPaymentGoods.SingleAsync();
        _product.DurationDays++; await _db.SaveChangesAsync();
        _gateway.Upload = new(0, []); await Tick();
        Assert.AreEqual(VirtualGoodsState.Failed, old.State); Assert.AreEqual("superseded", old.Error);
        Assert.AreEqual(1, _gateway.Starts); Assert.AreNotEqual(old.GoodsId, _gateway.Upload.Items.Single().Id);
    }

    [TestMethod]
    public async Task StatusPageUsesCurrentRevisionAndReportsFailureWithoutCredentials()
    {
        await Publish(); var state = (await Catalog().GetDisplayAsync([_product]))[_product.Id];
        Assert.AreEqual("ready", state.State); Assert.IsNotNull(state.PublishedAt);
        _product.PriceInFen++; await _db.SaveChangesAsync();
        Assert.AreEqual("pending", (await Catalog().GetDisplayAsync([_product]))[_product.Id].State);
        await Tick(); var row = await _db.VirtualPaymentGoods.SingleAsync(g => g.PriceInFen == _product.PriceInFen);
        row.State = VirtualGoodsState.Failed; row.Error = "platform-rejected"; await _db.SaveChangesAsync();
        Assert.AreEqual("failed", (await Catalog().GetDisplayAsync([_product]))[_product.Id].State);
        Assert.AreEqual("platform-rejected", (await Catalog().GetDisplayAsync([_product]))[_product.Id].Error);
    }

    [TestMethod]
    public async Task CrashBeforeSubmittingRecoversWithANewIdAndPreservesUncertainAttempt()
    {
        _gateway.ThrowBeforeStart = true;
        await Worker(_db).RunOnceAsync();
        var old = await _db.VirtualPaymentGoods.SingleAsync(); var originalId = old.GoodsId;
        _gateway.ThrowBeforeStart = false;
        await Tick(); Assert.AreEqual(VirtualGoodsState.Failed, old.State);
        Assert.AreEqual("task-unconfirmed", old.Error);
        await Tick();
        var next = await _db.VirtualPaymentGoods.SingleAsync(g => g.Attempt == 2);
        Assert.AreNotEqual(originalId, next.GoodsId); Assert.AreEqual(originalId, old.GoodsId);
        Assert.AreEqual(VirtualGoodsState.Uploading, next.State);
        await Tick(); await Tick(); await Tick();
        Assert.AreEqual(VirtualGoodsState.Published, (await _db.VirtualPaymentGoods.SingleAsync(g => g.Attempt == 2)).State);
        Assert.AreEqual(VirtualGoodsState.Failed, old.State);
    }

    [TestMethod]
    public async Task TwoLiveReplicasShareOnePlatformTaskSlot()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _gateway.BeforeQuery = async () => { entered.TrySetResult(); await release.Task; };
        var first = Worker(_db).RunOnceAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await using var other = new SqliteContext(_dbOptions);
            await Worker(other).RunOnceAsync();
            Assert.AreEqual(1, _gateway.Queries); Assert.AreEqual(0, _gateway.Starts);
        }
        finally { release.TrySetResult(); await first; }
        Assert.AreEqual(1, _gateway.Starts); Assert.AreEqual(1, await _db.VirtualPaymentGoods.CountAsync());
    }

    [TestMethod]
    public async Task UnconfirmedAttemptsStopAfterFiveNewIdentifiers()
    {
        _gateway.ThrowBeforeStart = true;
        await Worker(_db).RunOnceAsync();
        for (var i = 0; i < 9; i++) await Tick();
        await Tick();
        Assert.AreEqual(5, _gateway.Starts); Assert.AreEqual(5, await _db.VirtualPaymentGoods.CountAsync());
        Assert.AreEqual(5, await _db.VirtualPaymentGoods.Select(g => g.GoodsId).Distinct().CountAsync());
        Assert.AreEqual("failed", (await Catalog().GetDisplayAsync([_product]))[_product.Id].State);
    }

    [TestMethod]
    public async Task StolenLeaseCannotSubmitOrOverwriteTheNewOwnersState()
    {
        var replacement = Guid.NewGuid();
        _gateway.BeforeQuery = async () =>
        {
            await using var other = new SqliteContext(_dbOptions);
            await other.VirtualPaymentGoodsLeases.ExecuteUpdateAsync(s => s
                .SetProperty(l => l.Owner, replacement).SetProperty(l => l.Until, DateTime.UtcNow.AddMinutes(5)));
        };
        await Worker(_db).RunOnceAsync();
        Assert.AreEqual(0, _gateway.Starts);
        await using var verify = new SqliteContext(_dbOptions);
        Assert.AreEqual(VirtualGoodsState.Queued, (await verify.VirtualPaymentGoods.SingleAsync()).State);
        var lease = await verify.VirtualPaymentGoodsLeases.SingleAsync();
        Assert.AreEqual(replacement, lease.Owner); Assert.IsTrue(lease.Until > DateTime.UtcNow);
    }

    private VirtualPaymentGoodsSynchronizer Worker(SqliteContext db, IOptions<AppSettings>? options = null) =>
        new(db, options ?? _options, _gateway, NullLogger<VirtualPaymentGoodsSynchronizer>.Instance);
    private VirtualPaymentGoodsCatalog Catalog() => new(_db, _options);
    private async Task Tick()
    {
        await _db.VirtualPaymentGoods.Where(g => g.State != VirtualGoodsState.Published)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.NextAttemptAt, DateTime.UtcNow.AddSeconds(-1)));
        // New scopes in production reload persisted rows. Clear the old tracked
        // task state while keeping the product object for the assertions.
        await using var nextScope = new SqliteContext(_dbOptions);
        await Worker(nextScope).RunOnceAsync();
        foreach (var entry in _db.ChangeTracker.Entries<VirtualPaymentGoods>().ToList()) await entry.ReloadAsync();
    }
    private async Task Publish()
    {
        await Worker(_db).RunOnceAsync(); await Tick(); await Tick(); await Tick();
        var row = await _db.VirtualPaymentGoods.SingleAsync();
        row.PublishedAt = DateTime.UtcNow.AddMinutes(-11); await _db.SaveChangesAsync();
    }
    private static IOptions<AppSettings> Settings(bool enabled = true, int environment = 0, string app = "app", string offer = "offer") =>
        Options.Create(new AppSettings { AuthProvider = "Local", OIDC = new() { Authority = "https://example.test", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" }, Local = new(), WeChatEnabled = true, WeChat = new WeChatSettings
        {
            AppId = app, AppSecret = "not-a-secret", VirtualPayment = new VirtualPaymentSettings
            {
                Environment = environment, OfferId = offer, AppKey = "not-a-key", SandboxAppKey = "not-a-key",
                GoodsSync = new() { Enabled = enabled, PictureUrl = "https://example.test/goods.png" }
            }
        } });

    private sealed class FakeGoodsGateway : IVirtualPaymentGoodsGateway
    {
        public int Starts { get; private set; }
        public int Queries { get; private set; }
        public bool ThrowAfterStart { get; set; }
        public bool ThrowBeforeStart { get; set; }
        public GoodsTaskResult Upload { get; set; } = new(0, []);
        private GoodsTaskResult Publish { get; set; } = new(0, []);
        public Func<Task>? BeforeQuery { get; set; }
        public async Task<GoodsTaskResult> QueryAsync(bool publish, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Queries++;
            if (BeforeQuery != null) await BeforeQuery();
            return publish ? Publish : Upload;
        }
        public Task StartAsync(VirtualPaymentGoods goods, bool publish, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Starts++;
            if (ThrowBeforeStart) throw new HttpRequestException("Request was never submitted.");
            var result = new GoodsTaskResult(3, [new(goods.GoodsId, 2, goods.Name, goods.PriceInFen, goods.Remark)]);
            if (publish) Publish = result; else Upload = result;
            if (ThrowAfterStart) throw new HttpRequestException("A private URL that must not be persisted.");
            return Task.CompletedTask;
        }
    }
}
