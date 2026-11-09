using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentCheckoutTests
{
    private SqliteContext _db = null!;
    private LoginHandler _handler = null!;
    private DbContextOptions<SqliteContext> _dbOptions = null!;
    private User _buyer = null!;
    private VipProduct _product = null!;
    private Coupon _coupon = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _dbOptions = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite($"Data Source=checkout-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10").Options;
        _db = new SqliteContext(_dbOptions);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _handler = new LoginHandler();
        _buyer = new User { Id = Guid.NewGuid().ToString(), UserName = "buyer", DisplayName = "Buyer", MiniProgramOpenId = "buyer-openid", SessionKey = "old-session" };
        _product = new VipProduct { Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        _coupon = new Coupon { Code = "VIRTUAL120", AmountInFen = 12000, IsSingleUse = true,
            DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } };
        _db.AddRange(_buyer, _product, _coupon);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [TestCleanup]
    public async Task Cleanup() { await _db.DisposeAsync(); _handler.Dispose(); }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task FirstPurchaseCommitsExactSnapshotBeforeExposingFreshSessionSignatures(int environment)
    {
        var result = await CreateService(environment: environment).CreateAsync(_buyer.Id, _product.Id, "fresh-login", " VIRTUAL120 ");
        Assert.AreEqual(PaymentOrderStatus.Pending, result.Status);
        Assert.AreEqual(7800, result.AmountInFen); Assert.AreEqual(12000, result.DiscountInFen);
        Assert.AreEqual("VIRTUAL120", result.AppliedCouponCode);
        Assert.IsNull(_db.Database.CurrentTransaction);
        _db.ChangeTracker.Clear();
        var order = await _db.PaymentOrders.SingleAsync();
        var snapshot = await _db.VirtualPaymentSnapshots.SingleAsync();
        Assert.AreEqual(PaymentChannel.WeChatVirtual, order.Channel);
        Assert.AreEqual(order.OutTradeNo, result.OutTradeNo);
        Assert.AreEqual(order.Id, snapshot.PaymentOrderId);
        Assert.AreEqual("buyer-openid", snapshot.OpenId); Assert.AreEqual("test-app", snapshot.AppId);
        Assert.AreEqual(snapshot.SignData, result.PayParams!.SignData);
        using var json = JsonDocument.Parse(snapshot.SignData);
        Assert.AreEqual(19800, json.RootElement.GetProperty("goodsPrice").GetInt32());
        Assert.AreEqual(7800, json.RootElement.GetProperty("activitySellingPrice").GetInt32());
        Assert.AreEqual(environment, json.RootElement.GetProperty("env").GetInt32());
        Assert.AreEqual(Sign("requestVirtualPayment&" + snapshot.SignData, environment == 0 ? "live-key" : "sandbox-key"), result.PayParams.PaySig);
        Assert.AreEqual(Sign(snapshot.SignData, "fresh-session"), result.PayParams.Signature);
        Assert.AreNotEqual(Sign(snapshot.SignData, "old-session"), result.PayParams.Signature);
        Assert.AreEqual("old-session", (await _db.Users.SingleAsync()).SessionKey);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync());
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.AreEqual(1, _handler.Calls);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(_handler.Url!.Query);
        Assert.AreEqual("test-app", query["appid"].ToString());
        Assert.AreEqual("app-secret", query["secret"].ToString());
        Assert.AreEqual("fresh-login", query["js_code"].ToString());
        Assert.AreEqual("authorization_code", query["grant_type"].ToString());
        var response = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("fresh-session", response); Assert.DoesNotContain("app-secret", response);
        Assert.DoesNotContain("sandbox-key", response); Assert.DoesNotContain("live-key", response);
    }

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("wechat-off")]
    [DataRow("missing-user")]
    [DataRow("missing-openid")]
    [DataRow("empty-code")]
    [DataRow("large-code")]
    [DataRow("missing-appsecret")]
    public async Task InvalidPreconditionsDoNotConsumeLoginCodesOrCreateOrders(string fault)
    {
        if (fault == "missing-openid") await _db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.MiniProgramOpenId, (string?)null));
        var service = CreateService(enabled: fault != "disabled", weChatEnabled: fault != "wechat-off", secret: fault == "missing-appsecret" ? "" : "app-secret");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(fault == "missing-user" ? "missing" : _buyer.Id,
            _product.Id, fault == "empty-code" ? " " : fault == "large-code" ? new string('x', 513) : "fresh-login"));
        Assert.AreEqual(0, _handler.Calls); await AssertEmptyPurchase();
    }

    [TestMethod]
    [DataRow("openid")]
    [DataRow("missing-session")]
    [DataRow("platform")]
    [DataRow("network")]
    [DataRow("malformed")]
    public async Task FailedFreshLoginCannotChargeAnotherAccountOrExposeCredentials(string fault)
    {
        _handler.Fault = fault;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login"));
        Assert.DoesNotContain("sensitive", ex.ToString()); Assert.DoesNotContain("app-secret", ex.ToString());
        Assert.AreEqual("old-session", (await _db.Users.SingleAsync()).SessionKey);
        await AssertEmptyPurchase();
    }

    [TestMethod]
    public async Task ChangedBuyerAfterLoginCannotReserveAnOrder()
    {
        _handler.Hook = async () => { await _db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.MiniProgramOpenId, "changed")); };
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login"));
        await AssertEmptyPurchase();
    }

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi, PaymentOrderStatus.Pending)]
    [DataRow(PaymentChannel.WeChatJsApi, PaymentOrderStatus.Failed)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Pending)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Failed)]
    public async Task LocalExpirationDoesNotCloseOrReplaceAnEarlierPayment(PaymentChannel channel, PaymentOrderStatus status)
    {
        var old = new PaymentOrder { UserId = _buyer.Id, VipProductId = _product.Id, Channel = channel, Status = status,
            OutTradeNo = "OLD-ORDER", Description = "Old", AmountInFen = 19800, ExpiredAt = DateTime.UtcNow.AddDays(-1) };
        _db.Add(old); await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login"));
        Assert.AreEqual(1, await _db.PaymentOrders.CountAsync());
        Assert.AreEqual(status, (await _db.PaymentOrders.SingleAsync()).Status);
        Assert.AreEqual(0, await _db.VirtualPaymentSnapshots.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi)]
    [DataRow(PaymentChannel.WeChatVirtual)]
    public async Task ReservedSingleUseCouponStaysReservedAcrossChannelsAndUsers(PaymentChannel channel)
    {
        var other = new User { Id = Guid.NewGuid().ToString(), UserName = "other", DisplayName = "Other" };
        _db.Add(new PaymentOrder { User = other, UserId = other.Id, OutTradeNo = "OTHER-ORDER", Description = "Other",
            Channel = channel, CouponId = _coupon.Id, AmountInFen = 7800, ExpiredAt = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code));
        Assert.AreEqual(1, await _db.PaymentOrders.CountAsync());
        Assert.AreEqual(0, await _db.VirtualPaymentSnapshots.CountAsync());
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
    }

    [TestMethod]
    [DataRow("disabled-product")]
    [DataRow("negative-price")]
    [DataRow("missing-mapping")]
    [DataRow("wrong-price")]
    [DataRow("active-membership")]
    [DataRow("disabled-coupon")]
    [DataRow("claimed-by-other")]
    public async Task InvalidCatalogAndCouponConditionsCannotLeavePartialReservations(string fault)
    {
        if (fault == "disabled-product") await _db.VipProducts.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsEnabled, false));
        if (fault == "negative-price") await _db.VipProducts.ExecuteUpdateAsync(s => s.SetProperty(p => p.PriceInFen, -1));
        if (fault == "disabled-coupon") await _db.Coupons.ExecuteUpdateAsync(s => s.SetProperty(p => p.IsEnabled, false));
        if (fault == "active-membership") _db.Add(new VipMembership { UserId = _buyer.Id, VipProductId = _product.Id, StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1) });
        if (fault == "claimed-by-other")
        {
            var other = new User { Id = Guid.NewGuid().ToString(), UserName = "other", DisplayName = "Other" };
            _db.Add(new UserClaimedCoupon { User = other, UserId = other.Id, CouponId = _coupon.Id });
        }
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService(mapping: fault != "missing-mapping", price: fault == "wrong-price" ? 19801 : 19800)
            .CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code));
        Assert.AreEqual(0, await _db.PaymentOrders.CountAsync()); Assert.AreEqual(0, await _db.VirtualPaymentSnapshots.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync()); Assert.AreEqual(0, await _db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task ExplicitCouponWinsOverTheBestPreviouslyClaimedCoupon()
    {
        var best = new Coupon { Code = "OLDER130", AmountInFen = 13000, IsSingleUse = false, DistributionChannelId = _coupon.DistributionChannelId };
        _db.Add(new UserClaimedCoupon { UserId = _buyer.Id, Coupon = best, CouponId = best.Id });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        var result = await CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code);
        Assert.AreEqual(7800, result.AmountInFen); Assert.AreEqual(_coupon.Code, result.AppliedCouponCode);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ZeroAmountCommitsRightsAndAccountingWithoutVirtualParameters(bool failAccounting)
    {
        await _db.Coupons.ExecuteUpdateAsync(s => s.SetProperty(c => c.AmountInFen, 25000));
        _db.Add(new UserClaimedCoupon { UserId = _buyer.Id, CouponId = _coupon.Id }); await _db.SaveChangesAsync();
        if (failAccounting) await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_checkout BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;");
        if (failAccounting)
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code));
            _db.ChangeTracker.Clear(); await AssertEmptyPurchase();
            Assert.IsFalse((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        }
        else
        {
            var result = await CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code);
            Assert.AreEqual(PaymentOrderStatus.Paid, result.Status); Assert.AreEqual(0, result.AmountInFen);
            Assert.AreEqual(19800, result.DiscountInFen); Assert.IsNull(result.PayParams);
            Assert.AreEqual(0, await _db.VirtualPaymentSnapshots.CountAsync()); Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
            Assert.AreEqual(1, await _db.CouponUsages.CountAsync()); Assert.AreEqual(1, await _db.Changes.CountAsync());
            Assert.AreEqual(ChangeType.VipActivatedViaCoupon, (await _db.Changes.SingleAsync()).Type);
            Assert.AreEqual(_buyer.Id, (await _db.Coupons.SingleAsync()).UsedByUserId);
        }
    }

    [TestMethod]
    public async Task SnapshotInsertFailureRollsBackTheOrderAndCouponReservation()
    {
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_snapshot BEFORE INSERT ON VirtualPaymentSnapshots BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code));
        _db.ChangeTracker.Clear(); await AssertEmptyPurchase();
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_snapshot;");
        Assert.IsNotNull((await CreateService().CreateAsync(_buyer.Id, _product.Id, "another-code", _coupon.Code)).PayParams);
    }

    [TestMethod]
    public async Task RepeatedSeparateRequestDoesNotExposeOrReplaceTheSamePayableNumber()
    {
        var service = CreateService(); var first = await service.CreateAsync(_buyer.Id, _product.Id, "first-code");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(_buyer.Id, _product.Id, "second-code"));
        Assert.AreEqual(first.OutTradeNo, (await _db.PaymentOrders.SingleAsync()).OutTradeNo);
        Assert.AreEqual(1, await _db.VirtualPaymentSnapshots.CountAsync());
    }

    [TestMethod]
    public async Task CallerOwnedTransactionIsRejectedBeforeConsumingLoginCode()
    {
        await using (await _db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login"));
        Assert.AreEqual(0, _handler.Calls); await AssertEmptyPurchase();
        using (new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login"));
        Assert.AreEqual(0, _handler.Calls); await AssertEmptyPurchase();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationBeforeReservationCannotLeavePayableData(bool duringLogin)
    {
        using var cancellation = new CancellationTokenSource();
        if (duringLogin) _handler.Hook = cancellation.CancelAsync;
        else await cancellation.CancelAsync();
        var token = cancellation.Token;
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().CreateAsync(_buyer.Id, _product.Id, "fresh-login", cancellationToken: token));
        Assert.AreEqual(duringLogin ? 1 : 0, _handler.Calls); await AssertEmptyPurchase();
    }

    [TestMethod]
    public async Task SeparateConcurrentRequestsCannotReserveTwoPaymentsForTheSamePurchase()
    {
        var arrivals = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Hook = async () => { if (Interlocked.Increment(ref arrivals) == 2) gate.SetResult(); await gate.Task; };
        await using var first = new SqliteContext(_dbOptions);
        await using var second = new SqliteContext(_dbOptions);
        async Task<bool> Reserve(SqliteContext db)
        {
            try { await CreateService(db).CreateAsync(_buyer.Id, _product.Id, "fresh-login", _coupon.Code); return true; }
            catch (InvalidOperationException) { return false; }
            catch (System.Data.Common.DbException) { return false; }
        }
        var results = await Task.WhenAll(Reserve(first), Reserve(second));
        Assert.AreEqual(1, results.Count(r => r));
        Assert.AreEqual(1, await _db.PaymentOrders.CountAsync()); Assert.AreEqual(1, await _db.VirtualPaymentSnapshots.CountAsync());
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync()); Assert.AreEqual(0, await _db.Changes.CountAsync());
    }

    private VirtualPaymentCheckoutService CreateService(SqliteContext? db = null, int environment = 1, bool enabled = true,
        bool weChatEnabled = true, string secret = "app-secret", bool mapping = true, int price = 19800)
    {
        db ??= _db;
        var settings = Options.Create(new AppSettings
        {
            AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = weChatEnabled,
            OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
            WeChat = new WeChatSettings { AppId = "test-app", AppSecret = secret, VirtualPayment = new VirtualPaymentSettings
            { Enabled = enabled, Environment = environment, OfferId = "offer", SandboxAppKey = "sandbox-key", AppKey = "live-key",
                Products = mapping ? new() { [_product.Id.ToString()] = new() { ProductId = "course", PriceInFen = price } } : new() } }
        });
        return new VirtualPaymentCheckoutService(db, new CouponService(db),
            new PaymentFulfillmentService(db, new CouponService(db), new ChangeService(db), NullLogger<PaymentFulfillmentService>.Instance),
            new VirtualPaymentParameterService(settings), settings, new LoginFactory(_handler));
    }

    private async Task AssertEmptyPurchase()
    {
        Assert.AreEqual(0, await _db.PaymentOrders.CountAsync()); Assert.AreEqual(0, await _db.VirtualPaymentSnapshots.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync()); Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync()); Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
    }

    private static string Sign(string data, string key) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data)));
    private sealed class LoginFactory(LoginHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class LoginHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Url { get; private set; }
        public string? Fault { get; set; }
        public Func<Task>? Hook { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Url = request.RequestUri;
            if (Hook != null) await Hook();
            if (Fault == "network") throw new HttpRequestException("sensitive " + request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fault == "malformed" ? "sensitive" :
                JsonSerializer.Serialize(new { errcode = Fault == "platform" ? 40029 : 0, errmsg = "sensitive",
                    openid = Fault == "openid" ? "another-openid" : "buyer-openid", session_key = Fault == "missing-session" ? "" : "fresh-session" })) };
        }
    }
}
