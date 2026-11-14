using System.Net;
using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class AdminVirtualRefundTests
{
    private SqliteContext? _context;
    private SqliteContext _db => _context ?? throw new InvalidOperationException("The fixture has not started.");
    private DbContextOptions<SqliteContext> _dbOptions = null!;
    private PaymentOrder _order = null!;
    private MemoryCache? _cache;
    private VirtualPaymentAccessTokenService? _tokens;
    private IOptions<AppSettings> _settings = null!;
    private readonly PurchaseQuery _purchases = new();
    private readonly RefundGateway _gateway = new();

    [TestInitialize]
    public async Task Setup()
    {
        _dbOptions = new DbContextOptionsBuilder<SqliteContext>().UseSqlite($"Data Source=staff-refund-{Guid.NewGuid():N};Mode=Memory;Cache=Shared").Options;
        _context = new SqliteContext(_dbOptions); await _db.Database.OpenConnectionAsync(); await _db.Database.EnsureCreatedAsync();
        _cache = new MemoryCache(new MemoryCacheOptions());
        var user = new User { UserName = "synthetic-buyer", DisplayName = "Synthetic buyer", MiniProgramOpenId = "saved-openid", SessionKey = "synthetic-session" };
        var product = new VipProduct { Name = "Course", PriceInFen = 19800, DurationDays = 366, TotalCategory = new TotalCategory { Title = "Course" } };
        var coupon = new Coupon { Code = "SYNTHETIC", IsSingleUse = true, AmountInFen = 19600, DistributionChannel = new DistributionChannel { AgencyName = "Agency" } };
        _order = new PaymentOrder { User = user, UserId = user.Id, VipProduct = product, Coupon = coupon,
            OutTradeNo = "STAFF-REFUND", Description = "Synthetic", Channel = PaymentChannel.WeChatVirtual, AmountInFen = 200, DiscountInFen = 19600 };
        _db.AddRange(_order, new UserClaimedCoupon { UserId = user.Id, CouponId = coupon.Id }); await _db.SaveChangesAsync();
        _settings = Options.Create(new AppSettings { AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = true,
            OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
            WeChat = new WeChatSettings { AppId = "test-app", AppSecret = "synthetic-secret", VirtualPayment = new VirtualPaymentSettings {
                Enabled = true, Environment = 1, OfferId = "offer", SandboxAppKey = "synthetic-key",
                Products = new() { [product.Id.ToString()] = new() { ProductId = "course", PriceInFen = 19800 } } } } });
        var snapshot = new VirtualPaymentParameterService(_settings).Capture(_order, user);
        snapshot.Platform = VirtualPaymentPlatform.WeChat; snapshot.PlatformOrderId = "platform-order"; _db.Add(snapshot); await _db.SaveChangesAsync();
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await new PaymentFulfillmentService(_db, new CouponService(_db), new ChangeService(_db), NullLogger<PaymentFulfillmentService>.Instance).CompleteAsync(_order, "transaction");
            _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = _order.Id, EventType = "VirtualPaymentConfirmed", RawData = JsonSerializer.Serialize(new { _order.OutTradeNo, PlatformOrderId = "platform-order" }) });
            await _db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        _tokens = new VirtualPaymentAccessTokenService(_settings, new TokenFactory(), _cache);
        _purchases.Order = _order; _gateway.Db = _db; _db.ChangeTracker.Clear();
    }

    [TestCleanup]
    public async Task Cleanup() { _tokens?.Dispose(); _cache?.Dispose(); if (_context != null) await _context.DisposeAsync(); }

    private AdminVirtualRefundService Service(SqliteContext? db = null) => new(db ?? _db, _purchases, _gateway, _tokens!,
        new VirtualPaymentRefundService(db ?? _db), _settings, NullLogger<AdminVirtualRefundService>.Instance);

    [TestMethod]
    public async Task RefundIsDispatchedOnceAfterCommitAndOnlyConfirmationReversesItsRights()
    {
        Assert.AreEqual(AdminRefundResult.Accepted, await Service().RequestAsync(_order.Id, 200, "operator"));
        Assert.AreEqual(AdminRefundResult.Existing, await Service().RequestAsync(_order.Id, 200, "operator"));
        Assert.AreEqual(1, _gateway.Submissions);
        Assert.IsTrue(await _db.VipMemberships.AnyAsync(v => v.EndTime > DateTime.UtcNow));
        var request = await _db.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync();
        Assert.AreEqual(200, request.AmountInFen); Assert.AreEqual("operator", request.ActorId);
        Assert.AreEqual(VirtualPaymentRefundRequestState.Accepted, request.State);
        _gateway.State = VirtualRefundState.Completed;
        Assert.AreEqual(AdminRefundResult.Completed, await Service().CheckAsync(_order.Id, "operator"));
        Assert.AreEqual(AdminRefundResult.Completed, await Service().CheckAsync(_order.Id, "operator"));
        Assert.AreEqual(1, _gateway.Queries);
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        var membership = await _db.VipMemberships.AsNoTracking().SingleAsync(); Assert.AreEqual(membership.StartTime, membership.EndTime);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefunded"));
        Assert.AreEqual(1, await _db.Changes.CountAsync()); Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.IsTrue((await _db.UserClaimedCoupons.AsNoTracking().SingleAsync()).IsUsed);
        Assert.AreEqual(VirtualPaymentRefundRequestState.Completed, (await _db.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync()).State);
    }

    [TestMethod]
    public async Task LostResponseSurvivesRestartAsQueryOnlyAndCannotDispatchAnotherRefund()
    {
        _gateway.LoseResponse = true;
        Assert.AreEqual(AdminRefundResult.Unavailable, await Service().RequestAsync(_order.Id, 200, "operator"));
        await using var restarted = new SqliteContext(_dbOptions);
        Assert.AreEqual(AdminRefundResult.Existing, await Service(restarted).RequestAsync(_order.Id, 200, "another-operator"));
        Assert.AreEqual(1, _gateway.Submissions);
        Assert.AreEqual(VirtualPaymentRefundRequestState.Unconfirmed, (await restarted.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync()).State);
        _gateway.State = VirtualRefundState.Completed;
        Assert.AreEqual(AdminRefundResult.Completed, await Service(restarted).CheckAsync(_order.Id, "another-operator"));
        Assert.AreEqual(1, _gateway.Submissions);
    }

    [TestMethod]
    [DataRow(VirtualPaymentPlatform.Apple)]
    [DataRow(null)]
    public async Task AppleAndUnverifiedPaymentsNeverEnterTheRefundGateway(VirtualPaymentPlatform? platform)
    {
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(v => v.Platform, platform));
        var result = await Service().RequestAsync(_order.Id, 200, "operator");
        Assert.AreEqual(platform == VirtualPaymentPlatform.Apple ? AdminRefundResult.AppleManaged : AdminRefundResult.NotEligible, result);
        Assert.AreEqual(0, _gateway.Submissions); Assert.AreEqual(0, _purchases.Calls);
        Assert.AreEqual(0, await _db.VirtualPaymentRefundRequests.CountAsync());
    }

    [TestMethod]
    public async Task VerificationRecordsOnlyTheTrustedPaymentTypeAndNoFinancialChanges()
    {
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(v => v.Platform, (VirtualPaymentPlatform?)null));
        _purchases.Platform = VirtualPaymentPlatform.Apple;
        Assert.AreEqual(AdminRefundResult.Verified, await Service().VerifyPlatformAsync(_order.Id, "operator"));
        Assert.AreEqual(VirtualPaymentPlatform.Apple, (await _db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync()).Platform);
        Assert.AreEqual(0, _gateway.Submissions); Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow("amount")]
    [DataRow("partial")]
    [DataRow("platform")]
    [DataRow("identity")]
    [DataRow("not-delivered")]
    [DataRow("closed")]
    [DataRow("zero")]
    [DataRow("expired")]
    public async Task UnsafeOrChangedPurchasesCannotCreateAnIntent(string fault)
    {
        if (fault == "partial") _purchases.Remaining = 100;
        if (fault == "platform") _purchases.Platform = VirtualPaymentPlatform.Apple;
        if (fault == "identity") _purchases.Reference = "other-platform";
        if (fault == "not-delivered") _purchases.State = VirtualPaymentOrderState.Delivering;
        if (fault == "closed") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Closed));
        if (fault == "zero") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.AmountInFen, 0));
        if (fault == "expired") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.PaidAt, DateTime.UtcNow.AddDays(-366)));
        Assert.AreEqual(AdminRefundResult.NotEligible, await Service().RequestAsync(_order.Id, fault == "amount" ? 19800 : 200, "operator"));
        Assert.AreEqual(0, _gateway.Submissions); Assert.AreEqual(0, await _db.VirtualPaymentRefundRequests.CountAsync());
    }

    [TestMethod]
    public async Task ChangedMembershipIsPreservedWithASeparateReviewAfterMoneyRefunds()
    {
        await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.EndTime, v => v.EndTime.AddDays(30)));
        var end = (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime;
        await Service().RequestAsync(_order.Id, 200, "operator"); _gateway.State = VirtualRefundState.Completed;
        Assert.AreEqual(AdminRefundResult.Completed, await Service().CheckAsync(_order.Id, "operator"));
        Assert.AreEqual(end, (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefundReview"));
        Assert.AreEqual(VirtualPaymentRefundRequestState.Completed, (await _db.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync()).State);
    }

    [TestMethod]
    public async Task CallbackCompletionCannotBeOverwrittenByASlowerSubmissionResponse()
    {
        _gateway.BeforeResponse = async () =>
        {
            await using var other = new SqliteContext(_dbOptions);
            await other.VirtualPaymentRefundRequests.ExecuteUpdateAsync(s => s.SetProperty(r => r.State, VirtualPaymentRefundRequestState.Completed));
        };
        Assert.AreEqual(AdminRefundResult.Completed, await Service().RequestAsync(_order.Id, 200, "operator"));
        Assert.AreEqual(VirtualPaymentRefundRequestState.Completed, (await _db.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync()).State);
        Assert.AreEqual(1, _gateway.Submissions);
    }

    private sealed class PurchaseQuery : IVirtualPaymentQueryService
    {
        public PaymentOrder Order { get; set; } = null!;
        public int Calls { get; private set; }
        public VirtualPaymentPlatform Platform { get; set; } = VirtualPaymentPlatform.WeChat;
        public int Remaining { get; set; } = 200;
        public string Reference { get; set; } = "platform-order";
        public VirtualPaymentOrderState State { get; set; } = VirtualPaymentOrderState.Delivered;
        public Task<VirtualPaymentQueryResult> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot, string accessToken, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new VirtualPaymentQueryResult(State, Reference, "transaction", Order.Id, 200, Platform, Remaining, 200));
        }
    }

    private sealed class RefundGateway : IVirtualPaymentRefundGateway
    {
        public SqliteContext Db { get; set; } = null!;
        public int Submissions { get; private set; }
        public int Queries { get; private set; }
        public bool LoseResponse { get; set; }
        public VirtualRefundState State { get; set; }
        public Func<Task>? BeforeResponse { get; set; }
        public async Task<VirtualRefundSubmission> StartAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot, VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default)
        {
            Submissions++;
            Assert.IsNull(Db.Database.CurrentTransaction); Assert.AreEqual(1, await Db.VirtualPaymentRefundRequests.CountAsync());
            if (BeforeResponse != null) await BeforeResponse();
            if (LoseResponse) throw new InvalidOperationException("Synthetic lost response.");
            return new(true, null, "refund-platform");
        }
        public Task<VirtualRefundObservation> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot, VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default)
        {
            Queries++; return Task.FromResult(new VirtualRefundObservation(State, "refund-platform"));
        }
    }

    private sealed class TokenFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TokenHandler());
    }
    private sealed class TokenHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"synthetic-token\",\"expires_in\":7200}") });
    }
}
