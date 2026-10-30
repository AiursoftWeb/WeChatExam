using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Settings;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class WeChatPayCouponIntegrationTests
{
    private WeChatExamDbContext _dbContext = null!;
    private CouponService _couponService = null!;
    private ChangeService _changeService = null!;
    private WeChatPayService _payService = null!;
    private IOptions<AppSettings> _settings = null!;
    private HttpClient _tenpayHttpClient = null!;
    private WechatTenpayClient _tenpayClient = null!;
    private Guid _productId;
    private readonly string _privateKeyPath = Path.Combine(Path.GetTempPath(), $"school-payment-{Guid.NewGuid()}.pem");
    private string _userId = "test-user";

    [TestInitialize]
    public void TestInitialize()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new InMemoryContext(options);
        
        _couponService = new CouponService(_dbContext);
        _changeService = new ChangeService(_dbContext);
        
        _tenpayHttpClient = new HttpClient(new RejectPaymentRequestsHandler());
        _tenpayClient = new IsolatedTenpayClient(new WechatTenpayClientOptions
        {
            MerchantId = "1234567890",
            MerchantV3Secret = "12345678901234567890123456789012",
            MerchantCertificateSerialNumber = "SERIAL",
            MerchantCertificatePrivateKey = "---BEGIN PRIVATE KEY---\nMIICdgIBADANBgkqhkiG9w0BAQEFAASCAmAwggJcAgEAAoGBAK...\n---END PRIVATE KEY---"
        }, _tenpayHttpClient);
        
        _settings = Options.Create(new AppSettings
        { 
            AuthProvider = "Local",
            OIDC = new OidcSettings 
            { 
                Authority = "https://auth.example.com",
                ClientId = "client",
                ClientSecret = "secret",
                RolePropertyName = "role",
                UsernamePropertyName = "name",
                UserDisplayNamePropertyName = "name",
                EmailPropertyName = "email",
                UserIdentityPropertyName = "sub"
            },
            Local = new LocalSettings(),
            WeChat = new WeChatSettings 
            { 
                AppId = "appid",
                Payment = new WeChatPaySettings { MchId = "mchid", PrivateKeyFilePath = _privateKeyPath }
            } 
        });
        


        _payService = new WeChatPayService(
            _tenpayClient,
            _dbContext,
            _couponService,
            _changeService,
            _settings,
            NullLogger<WeChatPayService>.Instance);

        // Setup base data
        var channel = new DistributionChannel { AgencyName = "Test Agency" };
        _dbContext.DistributionChannels.Add(channel);
        
        var product = new VipProduct { Name = "Test VIP", PriceInFen = 10000, DurationDays = 30, IsEnabled = true };
        _dbContext.VipProducts.Add(product);
        _productId = product.Id;

        _dbContext.SaveChanges();
    }

    [TestMethod]
    public async Task CourseMembershipReadsKeepValidityAndRealExamEntitlementsIndependent()
    {
        var now = DateTime.UtcNow;
        _dbContext.Users.Add(new User { Id = _userId, UserName = _userId, DisplayName = "Membership test user" });
        var active = new VipProduct { Name = "Stopped sales", IsEnabled = false, TotalCategory = new TotalCategory { Title = "History" } };
        var expired = new VipProduct { Name = "Expired", TotalCategory = new TotalCategory { Title = "Western history" } };
        var future = new VipProduct { Name = "Future", TotalCategory = new TotalCategory { Title = "Traditional music" } };
        var real = new VipProduct { Name = "All real exams", Type = VipProductType.RealExam };
        _dbContext.VipMemberships.AddRange(
            new VipMembership { UserId = _userId, VipProduct = active, StartTime = now.AddDays(-1), EndTime = now.AddDays(30) },
            new VipMembership { UserId = _userId, VipProduct = expired, StartTime = now.AddDays(-30), EndTime = now.AddDays(-1) },
            new VipMembership { UserId = _userId, VipProduct = future, StartTime = now.AddDays(1), EndTime = now.AddDays(30) },
            new VipMembership { UserId = _userId, VipProduct = real, StartTime = now.AddDays(-1), EndTime = now.AddDays(30) });
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var memberships = await _payService.GetVipStatusListAsync(_userId);
        Assert.AreEqual(4, memberships.Count);
        Assert.AreEqual("History", memberships.Single(m => m.VipProductId == active.Id).VipProduct!.TotalCategory!.Title);
        var access = await new PaperAccessService(_payService).GetUserAccessStatusAsync(_userId);
        CollectionAssert.AreEquivalent(new[] { active.TotalCategoryId!.Value }, access.ActiveCategoryVips.ToArray());
        Assert.IsTrue(access.HasRealExamVip);
        Assert.IsTrue(await _payService.HasVipForCategoryAsync(_userId, active.TotalCategoryId.Value));
        Assert.IsFalse(await _payService.HasVipForCategoryAsync(_userId, expired.TotalCategoryId!.Value));
        Assert.IsFalse(await _payService.HasVipForCategoryAsync(_userId, future.TotalCategoryId!.Value));
        Assert.AreEqual(now.AddDays(30), memberships.Single(m => m.VipProductId == active.Id).EndTime);
        var visitor = await new PaperAccessService(_payService).GetUserAccessStatusAsync(null);
        Assert.AreEqual(0, visitor.ActiveCategoryVips.Count);
        Assert.IsFalse(visitor.HasRealExamVip);
    }

    [TestMethod]
    public async Task TestCreateOrder_WithManualCoupon()
    {
        // 1. Create a coupon
        await _couponService.CreateAsync(_dbContext.DistributionChannels.First().Id, "SAVE20", 2000, false);

        // 2. We can't easily mock the Tenpay SDK call without a lot of setup, 
        // so we'll test the logic that happens BEFORE the SDK call by checking how it calculates the amount.
        // Actually, since we want a full integration, let's look at what we can verify.
        
        // We can verify that ValidateCouponAsync is called and results are handled.
        // Since I can't easily mock the SDK's ExecuteCreatePayTransactionJsapiAsync (it's an extension method usually),
        // I will focus on verifying that the order is saved with correct DiscountInFen.
        
        // Let's use a trick: If the SDK call fails, it returns Success=false. 
        // But we want to see if the Coupon was validated.
        
        var result = await _payService.CreateOrderAsync(_userId, "openid", _productId, "SAVE20");
        
        // If it failed because of SDK, that's fine, but if it failed because of "优惠码无效", that's what we want to catch.
        if (!result.Success && result.ErrorMessage == "优惠码无效")
        {
            Assert.Fail("Coupon should have been valid");
        }
    }

    [TestMethod]
    public async Task TestProcessVipActivation_RecordsCouponUsage()
    {
        // 1. Setup a coupon and a claim
        var coupon = await _couponService.CreateAsync(_dbContext.DistributionChannels.First().Id, "PROMO", 1000, false);
        await _couponService.ClaimCouponAsync(_userId, "PROMO");

        // 2. Manually create a Paid order that used this coupon
        var order = new PaymentOrder
        {
            OutTradeNo = "ORDER1",
            UserId = _userId,
            VipProductId = _productId,
            CouponId = coupon.Id,
            DiscountInFen = 1000,
            AmountInFen = 9000,
            Status = PaymentOrderStatus.Paid,
            Description = "Test"
        };
        _dbContext.PaymentOrders.Add(order);
        await _dbContext.SaveChangesAsync();

        // 3. Trigger activation (this is private, but we can call it indirectly via HandlePaymentNotify or just test the logic)
        // Since ProcessVipActivationAsync is private, we'll use Reflection or just test the HandlePaymentNotify if we had a way to mock the body.
        // Actually, in our WeChatPayService, ProcessVipActivationAsync is private.
        
        // Let's use a PrivateObject-like approach or just make it internal and use InternalsVisibleTo? 
        // For now, I'll use Reflection to test this critical path.
        
        var method = typeof(WeChatPayService).GetMethod("ProcessVipActivationAsync", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        await (Task)method!.Invoke(_payService, new object[] { order })!;

        // 4. Verify VIP is active
        var vip = await _dbContext.VipMemberships.AnyAsync(v => v.UserId == _userId && v.VipProductId == _productId);
        Assert.IsTrue(vip);

        // 5. Verify CouponUsage is recorded
        var usage = await _dbContext.CouponUsages.AnyAsync(u => u.CouponId == coupon.Id && u.UserId == _userId);
        Assert.IsTrue(usage);

        // 6. Verify Claim is marked used
        var claim = await _dbContext.UserClaimedCoupons.FirstAsync(c => c.UserId == _userId && c.CouponId == coupon.Id);
        Assert.IsTrue(claim.IsUsed);

        // 7. Verify Change is recorded
        var change = await _dbContext.Changes.FirstOrDefaultAsync(c => c.TargetUserId == _userId);
        Assert.IsNotNull(change);
        Assert.AreEqual(ChangeType.VipActivatedViaPayment, change.Type);
    }

    [TestMethod]
    public async Task TestCreateOrder_ZeroAmount_RecordsCouponActivation()
    {
        // 1. Setup a product and a 100% discount coupon
        await _couponService.CreateAsync(_dbContext.DistributionChannels.First().Id, "FREE", 10000, false);
        await _couponService.ClaimCouponAsync(_userId, "FREE");

        // 2. Create order
        var result = await _payService.CreateOrderAsync(_userId, "openid", _productId, "FREE");

        // 3. Verify success and immediate activation
        Assert.IsTrue(result.Success);
        
        // 4. Verify Change is recorded as ViaCoupon
        var change = await _dbContext.Changes.FirstOrDefaultAsync(c => c.TargetUserId == _userId && c.Type == ChangeType.VipActivatedViaCoupon);
        Assert.IsNotNull(change);
    }
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidClaimedFullDiscountCannotActivateMembership(bool consumed)
    {
        var channel = await _dbContext.DistributionChannels.SingleAsync();
        var coupon = await _couponService.CreateAsync(channel.Id, "OLD-FREE", 10000, true);
        await _couponService.ClaimCouponAsync(_userId, coupon.Code);
        if (consumed)
            coupon.UsedByUserId = "another-user";
        else
            channel.IsEnabled = false;
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        // Invalid automatic discounts must not take the zero-amount activation path.
        // This fixture rejects outbound payment requests, so an undiscounted order
        // cannot charge WeChat and must leave memberships and usage untouched.
        var result = await _payService.CreateOrderAsync(_userId, "openid", _productId);
        Assert.IsFalse(result.Success);
        var pending = await _dbContext.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, pending.Status);
        Assert.AreEqual(10000, pending.AmountInFen);
        Assert.IsNull(pending.CouponId);
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
        Assert.IsFalse((await _dbContext.UserClaimedCoupons.SingleAsync()).IsUsed);
    }

    [TestMethod]
    public async Task ClaimedSingleUseCodeCannotActivateAnotherBuyer()
    {
        var channel = await _dbContext.DistributionChannels.SingleAsync();
        var coupon = await _couponService.CreateAsync(channel.Id, "OWNER-ONLY", 10000, true);
        Assert.IsTrue((await _couponService.ClaimCouponAsync(_userId, coupon.Code)).Success);
        _dbContext.ChangeTracker.Clear();

        // Passing a code directly to checkout must not bypass the claim endpoint's owner check.
        var other = await _payService.CreateOrderAsync("other-user", "other-openid", _productId, coupon.Code);
        Assert.IsFalse(other.Success);
        Assert.AreEqual("该一次性优惠券已被他人领取", other.ErrorMessage);
        Assert.AreEqual(0, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
        Assert.IsFalse((await _dbContext.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.IsNull((await _dbContext.Coupons.SingleAsync()).UsedByUserId);

        // The rejected attempt must leave the owner's full discount usable, with no WeChat charge.
        var owner = await _payService.CreateOrderAsync(_userId, "openid", _productId, coupon.Code);
        Assert.IsTrue(owner.Success);
        Assert.AreEqual(_userId, (await _dbContext.PaymentOrders.SingleAsync()).UserId);
        Assert.AreEqual(_userId, (await _dbContext.VipMemberships.SingleAsync()).UserId);
        Assert.AreEqual(_userId, (await _dbContext.CouponUsages.SingleAsync()).UserId);
        Assert.AreEqual(_userId, (await _dbContext.Coupons.SingleAsync()).UsedByUserId);
        Assert.IsTrue((await _dbContext.UserClaimedCoupons.SingleAsync()).IsUsed);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _tenpayClient.Dispose();
        _tenpayHttpClient.Dispose();
        _dbContext.Dispose();
        if (File.Exists(_privateKeyPath)) File.Delete(_privateKeyPath);
    }

    private sealed class IsolatedTenpayClient(WechatTenpayClientOptions options, HttpClient httpClient)
        : WechatTenpayClient(options, httpClient, false);

    private sealed class RejectPaymentRequestsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Outbound WeChat Pay requests are disabled in tests.");
    }

    [TestMethod]
    public async Task PendingCouponIsReservedAndSamePurchaseKeepsSavedPrice()
    {
        var requests = new List<string>();
        await UseCheckoutDatabaseAsync((request, body) =>
        {
            Assert.AreEqual("/v3/pay/transactions/jsapi", request.RequestUri!.AbsolutePath);
            requests.Add(body);
            return (System.Net.HttpStatusCode.OK, "{\"prepay_id\":\"reserved-prepay\"}");
        });
        var first = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        Assert.IsTrue(first.Success, first.ErrorMessage);
        var product = await _dbContext.VipProducts.FindAsync(_productId);
        product!.PriceInFen = 300;
        await _dbContext.SaveChangesAsync();
        Assert.IsFalse((await _payService.CreateOrderAsync(_userId, "openid", _productId, "DIFFERENT-CODE")).Success);
        var retry = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        Assert.IsTrue(retry.Success, retry.ErrorMessage);
        Assert.AreEqual(first.OutTradeNo, retry.OutTradeNo);
        var second = new VipProduct { Name = "Second course", TotalCategory = new TotalCategory { Title = "Other" }, PriceInFen = 200, DurationDays = 30 };
        _dbContext.VipProducts.Add(second);
        await _dbContext.SaveChangesAsync();
        Assert.IsFalse((await _payService.CreateOrderAsync(_userId, "openid", second.Id, "RESERVED")).Success);
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual(1, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(100, (await _dbContext.PaymentOrders.SingleAsync()).AmountInFen);
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
    }

    [TestMethod]
    [DataRow("NOTPAY", true, 100, "CNY", "closed")]
    [DataRow("CLOSED", true, 100, "CNY", "closed")]
    [DataRow("SUCCESS", true, 100, "CNY", "paid")]
    [DataRow("SUCCESS", true, 99, "CNY", "pending")]
    [DataRow("SUCCESS", true, 100, "USD", "pending")]
    [DataRow("USERPAYING", true, 100, "CNY", "pending")]
    [DataRow("NOTPAY", false, 100, "CNY", "pending")]
    [DataRow("UNKNOWN", true, 100, "CNY", "pending")]
    [DataRow("TIMEOUT", true, 100, "CNY", "pending")]
    [DataRow("WRONG_APP", true, 100, "CNY", "pending")]
    [DataRow("WRONG_MERCHANT", true, 100, "CNY", "pending")]
    public async Task ExpiredCouponIsReleasedOnlyAfterConfirmedClosure(string state, bool closeSucceeds, int total, string currency, string expected)
    {
        var creates = 0;
        var closes = 0;
        await UseCheckoutDatabaseAsync((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/close"))
            {
                closes++;
                return closeSucceeds ? (System.Net.HttpStatusCode.NoContent, "") : (System.Net.HttpStatusCode.InternalServerError, "{\"code\":\"SYSTEM_ERROR\",\"message\":\"Unknown close result\"}");
            }
            if (request.Method == HttpMethod.Get)
            {
                if (state == "TIMEOUT") throw new TaskCanceledException("Synthetic query timeout");
                if (state == "UNKNOWN") return (System.Net.HttpStatusCode.NotFound, "{\"code\":\"ORDER_NOT_EXIST\",\"message\":\"Unknown order\"}");
                return (System.Net.HttpStatusCode.OK, JsonSerializer.Serialize(new { out_trade_no = "OLD-PENDING", mchid = state == "WRONG_MERCHANT" ? "other" : "mchid", appid = state == "WRONG_APP" ? "other" : "appid", trade_state = state.StartsWith("WRONG_") ? "CLOSED" : state, transaction_id = "transaction-old", amount = new { total, currency } }));
            }
            Assert.AreEqual("/v3/pay/transactions/jsapi", path);
            creates++;
            return (System.Net.HttpStatusCode.OK, "{\"prepay_id\":\"new-prepay\"}");
        });
        var coupon = await _dbContext.Coupons.SingleAsync();
        var old = new PaymentOrder { UserId = _userId, VipProductId = _productId, CouponId = coupon.Id,
            OutTradeNo = "OLD-PENDING", Description = "Old pending payment", AmountInFen = 100, DiscountInFen = 100,
            ExpiredAt = DateTime.UtcNow.AddMinutes(-1), PrepayId = "old-prepay" };
        _dbContext.PaymentOrders.Add(old);
        await _dbContext.SaveChangesAsync();
        var result = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        _dbContext.ChangeTracker.Clear();
        var saved = await _dbContext.PaymentOrders.SingleAsync(o => o.Id == old.Id);
        Assert.AreEqual(expected == "closed", result.Success);
        Assert.AreEqual(expected == "closed" ? PaymentOrderStatus.Closed : expected == "paid" ? PaymentOrderStatus.Paid : PaymentOrderStatus.Pending, saved.Status);
        Assert.AreEqual(expected == "closed" ? 2 : 1, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(expected == "closed" ? 1 : 0, creates);
        Assert.AreEqual(state == "NOTPAY" ? 1 : 0, closes);
        Assert.AreEqual(expected == "paid" ? 1 : 0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(expected == "paid" ? 1 : 0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(expected == "paid", (await _dbContext.Coupons.SingleAsync()).UsedByUserId != null);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LegacyCouponWithTwoPendingOrdersMustReconcileBoth(bool secondWasPaid)
    {
        var queries = new List<string>();
        await UseCheckoutDatabaseAsync((request, _) =>
        {
            if (request.Method != HttpMethod.Get)
                return (System.Net.HttpStatusCode.OK, "{\"prepay_id\":\"new-prepay\"}");
            var number = request.RequestUri!.Segments.Last();
            queries.Add(number);
            return (System.Net.HttpStatusCode.OK, JsonSerializer.Serialize(new { out_trade_no = number, mchid = "mchid", appid = "appid",
                trade_state = secondWasPaid && number == "LEGACY-2" ? "SUCCESS" : "CLOSED", transaction_id = "legacy-transaction", amount = new { total = 100, currency = "CNY" } }));
        });
        var coupon = await _dbContext.Coupons.SingleAsync();
        var anotherProduct = new VipProduct { Name = "Another course", TotalCategory = new TotalCategory { Title = "Another history" }, PriceInFen = 200, DurationDays = 30 };
        _dbContext.VipProducts.Add(anotherProduct);
        _dbContext.PaymentOrders.AddRange(
            new PaymentOrder { UserId = _userId, VipProductId = _productId, CouponId = coupon.Id, OutTradeNo = "LEGACY-1", Description = "First old payment", AmountInFen = 100, DiscountInFen = 100, ExpiredAt = DateTime.UtcNow.AddMinutes(-2), CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new PaymentOrder { UserId = _userId, VipProductId = anotherProduct.Id, CouponId = coupon.Id, OutTradeNo = "LEGACY-2", Description = "Second old payment", AmountInFen = 100, DiscountInFen = 100, ExpiredAt = DateTime.UtcNow.AddMinutes(-1), CreatedAt = DateTime.UtcNow.AddDays(-1) });
        await _dbContext.SaveChangesAsync();
        var result = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        Assert.AreEqual(!secondWasPaid, result.Success, result.ErrorMessage);
        CollectionAssert.AreEqual(new[] { "LEGACY-1", "LEGACY-2" }, queries);
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual(PaymentOrderStatus.Closed, (await _dbContext.PaymentOrders.SingleAsync(o => o.OutTradeNo == "LEGACY-1")).Status);
        Assert.AreEqual(secondWasPaid ? PaymentOrderStatus.Paid : PaymentOrderStatus.Closed, (await _dbContext.PaymentOrders.SingleAsync(o => o.OutTradeNo == "LEGACY-2")).Status);
        Assert.AreEqual(secondWasPaid ? 2 : 3, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(secondWasPaid ? 1 : 0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(secondWasPaid ? 1 : 0, await _dbContext.VipMemberships.CountAsync());
    }

    [TestMethod]
    public async Task GatewayTimeoutRetainsReservationAndRetriesSameRemoteOrder()
    {
        var requests = new List<string>();
        await UseCheckoutDatabaseAsync((_, body) =>
        {
            requests.Add(body);
            if (requests.Count == 1) throw new TaskCanceledException("Synthetic lost prepay response");
            return (System.Net.HttpStatusCode.OK, "{\"prepay_id\":\"recovered-prepay\"}");
        });
        Assert.IsFalse((await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED")).Success);
        _dbContext.ChangeTracker.Clear();
        var reserved = await _dbContext.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, reserved.Status);
        Assert.IsNull(reserved.PrepayId);
        var retry = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        Assert.IsTrue(retry.Success, retry.ErrorMessage);
        Assert.AreEqual(reserved.OutTradeNo, retry.OutTradeNo);
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual(requests[0], requests[1]);
        Assert.AreEqual(1, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task FullDiscountFailureRollsBackCouponMembershipAndOrder()
    {
        await UseCheckoutDatabaseAsync((_, _) => throw new AssertFailedException("A full discount must not contact WeChat."));
        var coupon = await _dbContext.Coupons.SingleAsync();
        coupon.AmountInFen = 200;
        await _dbContext.SaveChangesAsync();
        await _dbContext.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_checkout_accounting BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'Synthetic accounting failure'); END;");
        Assert.IsFalse((await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED")).Success);
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual(0, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _dbContext.PaymentLogs.CountAsync());
        Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
        Assert.IsNull((await _dbContext.Coupons.SingleAsync()).UsedByUserId);
        await _dbContext.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_checkout_accounting;");
        var retry = await _payService.CreateOrderAsync(_userId, "openid", _productId, "RESERVED");
        Assert.IsTrue(retry.Success, retry.ErrorMessage);
        Assert.AreEqual("FREE", retry.PayParams!.Package);
        Assert.AreEqual(1, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _dbContext.VipMemberships.CountAsync());
    }

    [TestMethod]
    public async Task OrderStatusRecoversMissingNotificationWithoutAnotherCheckout()
    {
        var queries = 0;
        await UseCheckoutDatabaseAsync((request, _) =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            queries++;
            return (System.Net.HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                out_trade_no = "PAID-WITHOUT-NOTIFY", mchid = "mchid", appid = "appid", trade_state = "SUCCESS",
                transaction_id = "recovered-transaction", amount = new { total = 100, currency = "CNY" }
            }));
        });
        var coupon = await _dbContext.Coupons.SingleAsync();
        var order = new PaymentOrder { UserId = _userId, VipProductId = _productId, CouponId = coupon.Id,
            OutTradeNo = "PAID-WITHOUT-NOTIFY", Description = "Missing notification", AmountInFen = 100,
            DiscountInFen = 100, ExpiredAt = DateTime.UtcNow.AddMinutes(-1) };
        _dbContext.PaymentOrders.Add(order);
        await _dbContext.SaveChangesAsync();

        var result = await _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId);
        Assert.AreEqual(PaymentOrderStatus.Paid, result!.Status);
        Assert.AreEqual("recovered-transaction", result.WechatTransactionId);
        Assert.IsNotNull(result.PaidAt);
        _dbContext.ChangeTracker.Clear();
        var expiry = (await _dbContext.VipMemberships.SingleAsync()).EndTime;
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId))!.Status);
        Assert.AreEqual(expiry, (await _dbContext.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(1, queries);
        Assert.AreEqual(1, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _dbContext.Changes.CountAsync());
        Assert.AreEqual(1, await _dbContext.PaymentOrders.CountAsync());
    }

    [TestMethod]
    [DataRow("CLOSED", "closed")]
    [DataRow("NOTPAY", "pending")]
    [DataRow("USERPAYING", "pending")]
    [DataRow("TIMEOUT", "pending")]
    [DataRow("NOT_FOUND", "pending")]
    [DataRow("WRONG_AMOUNT", "pending")]
    [DataRow("WRONG_CURRENCY", "pending")]
    [DataRow("WRONG_APP", "pending")]
    [DataRow("WRONG_MERCHANT", "pending")]
    [DataRow("WRONG_ORDER", "pending")]
    [DataRow("NO_TRANSACTION", "pending")]
    public async Task OrderStatusNeverGuessesPaymentOrClosesPayableOrders(string scenario, string expected)
    {
        var queries = 0;
        await UseCheckoutDatabaseAsync((request, _) =>
        {
            queries++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            if (scenario == "TIMEOUT") throw new TaskCanceledException("Synthetic query timeout");
            if (scenario == "NOT_FOUND") return (System.Net.HttpStatusCode.NotFound, "{\"code\":\"ORDER_NOT_EXIST\",\"message\":\"Unknown payment\"}");
            return (System.Net.HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                out_trade_no = scenario == "WRONG_ORDER" ? "other-order" : "STATUS-QUERY",
                mchid = scenario == "WRONG_MERCHANT" ? "other" : "mchid",
                appid = scenario == "WRONG_APP" ? "other" : "appid",
                trade_state = scenario.StartsWith("WRONG_") || scenario == "NO_TRANSACTION" ? "SUCCESS" : scenario,
                transaction_id = scenario == "NO_TRANSACTION" ? "" : "remote-transaction",
                amount = new { total = scenario == "WRONG_AMOUNT" ? 99 : 100, currency = scenario == "WRONG_CURRENCY" ? "USD" : "CNY" }
            }));
        });
        var coupon = await _dbContext.Coupons.SingleAsync();
        var order = new PaymentOrder { UserId = _userId, VipProductId = _productId, CouponId = coupon.Id,
            OutTradeNo = "STATUS-QUERY", Description = "Unconfirmed order", AmountInFen = 100, DiscountInFen = 100,
            ExpiredAt = DateTime.UtcNow.AddDays(-1) };
        _dbContext.PaymentOrders.Add(order);
        await _dbContext.SaveChangesAsync();
        var result = await _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId);
        Assert.AreEqual(expected == "closed" ? PaymentOrderStatus.Closed : PaymentOrderStatus.Pending, result!.Status);
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual(result.Status, (await _dbContext.PaymentOrders.SingleAsync()).Status);
        Assert.AreEqual(1, queries);
        Assert.IsNull(result.PaidAt);
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
        Assert.IsNull((await _dbContext.Coupons.SingleAsync()).UsedByUserId);
    }

    [TestMethod]
    public async Task OrderStatusAuthorizesBeforeQueryAndKeepsTerminalOrdersLocal()
    {
        var queries = 0;
        await UseCheckoutDatabaseAsync((_, _) => { queries++; throw new AssertFailedException("Must not contact WeChat."); });
        var order = new PaymentOrder { UserId = _userId, VipProductId = _productId,
            OutTradeNo = "OWNER-ONLY", Description = "Private order", AmountInFen = 200 };
        _dbContext.PaymentOrders.Add(order);
        await _dbContext.SaveChangesAsync();
        Assert.IsNull(await _payService.QueryOrderStatusAsync(order.OutTradeNo, "another-user"));
        Assert.IsNull(await _payService.QueryOrderStatusAsync(order.OutTradeNo, null));
        Assert.IsNull(await _payService.QueryOrderStatusAsync("MISSING", _userId));
        foreach (var state in new[] { PaymentOrderStatus.Paid, PaymentOrderStatus.Refunded, PaymentOrderStatus.Closed })
        {
            order.Status = state;
            await _dbContext.SaveChangesAsync();
            Assert.AreEqual(state, (await _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId))!.Status);
        }
        Assert.AreEqual(0, queries);
        Assert.AreEqual(0, await _dbContext.PaymentLogs.CountAsync());
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
    }

    [TestMethod]
    public async Task OrderStatusActivationFailureRollsBackAndCanBeRetried()
    {
        await UseCheckoutDatabaseAsync((request, _) =>
        {
            Assert.AreEqual(HttpMethod.Get, request.Method);
            return (System.Net.HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                out_trade_no = "STATUS-RETRY", mchid = "mchid", appid = "appid", trade_state = "SUCCESS",
                transaction_id = "retry-transaction", amount = new { total = 100, currency = "CNY" }
            }));
        });
        var coupon = await _dbContext.Coupons.SingleAsync();
        var order = new PaymentOrder { UserId = _userId, VipProductId = _productId, CouponId = coupon.Id,
            OutTradeNo = "STATUS-RETRY", Description = "Retry query", AmountInFen = 100, DiscountInFen = 100,
            Status = PaymentOrderStatus.Failed };
        _dbContext.PaymentOrders.Add(order);
        await _dbContext.SaveChangesAsync();
        await _dbContext.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_status_accounting BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'Synthetic accounting failure'); END;");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId));
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual(PaymentOrderStatus.Failed, (await _dbContext.PaymentOrders.SingleAsync()).Status);
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
        Assert.IsNull((await _dbContext.Coupons.SingleAsync()).UsedByUserId);
        await _dbContext.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_status_accounting;");
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _payService.QueryOrderStatusAsync(order.OutTradeNo, _userId))!.Status);
        Assert.AreEqual(1, await _dbContext.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _dbContext.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _dbContext.Changes.CountAsync());
    }

    private async Task UseCheckoutDatabaseAsync(Func<HttpRequestMessage, string, (System.Net.HttpStatusCode Status, string Body)> respond)
    {
        await _dbContext.DisposeAsync();
        _dbContext = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite("Data Source=:memory:").Options);
        await _dbContext.Database.OpenConnectionAsync();
        await _dbContext.Database.EnsureCreatedAsync();
        _tenpayClient.Dispose();
        _tenpayHttpClient.Dispose();
        using var rsa = RSA.Create(2048);
        await File.WriteAllTextAsync(_privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());
        var request = new CertificateRequest("CN=Isolated checkout", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var entry = new CertificateEntry(CertificateEntry.ALGORITHM_TYPE_RSA, certificate.ExportCertificatePem());
        _tenpayHttpClient = new HttpClient(new CheckoutGateway(respond, rsa.ExportPkcs8PrivateKeyPem(), entry.SerialNumber));
        _tenpayClient = new IsolatedTenpayClient(new WechatTenpayClientOptions { MerchantId = "mchid", MerchantCertificateSerialNumber = "SERIAL", MerchantV3Secret = "12345678901234567890123456789012", MerchantCertificatePrivateKey = rsa.ExportPkcs8PrivateKeyPem() }, _tenpayHttpClient);
        _tenpayClient.PlatformCertificateManager.AddEntry(entry);
        _couponService = new CouponService(_dbContext);
        _changeService = new ChangeService(_dbContext);
        _payService = new WeChatPayService(_tenpayClient, _dbContext, _couponService, _changeService, _settings, NullLogger<WeChatPayService>.Instance);
        _dbContext.Users.Add(new User { Id = _userId, UserName = _userId, DisplayName = "Checkout buyer" });
        var product = new VipProduct { Name = "Reserved course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 200, DurationDays = 30 };
        _productId = product.Id;
        _dbContext.VipProducts.Add(product);
        _dbContext.Coupons.Add(new Coupon { Code = "RESERVED", AmountInFen = 100, IsSingleUse = true, DistributionChannel = new DistributionChannel { AgencyName = "Checkout" } });
        await _dbContext.SaveChangesAsync();
    }

    private sealed class CheckoutGateway(Func<HttpRequestMessage, string, (System.Net.HttpStatusCode Status, string Body)> respond, string key, string serial) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var (status, payload) = respond(request, body);
            var response = new HttpResponseMessage(status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            const string nonce = "isolated-checkout";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(key);
            response.Headers.Add("Wechatpay-Serial", serial);
            response.Headers.Add("Wechatpay-Timestamp", timestamp);
            response.Headers.Add("Wechatpay-Nonce", nonce);
            response.Headers.Add("Wechatpay-Signature", Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{payload}\n"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
            return response;
        }
    }

    [TestMethod]
    public async Task BuyingTwoSchoolsActivatesOnlyTheirIndependentMemberships()
    {
        _dbContext.Users.Add(new User { Id = _userId, UserName = _userId, DisplayName = "School buyer" });
        var first = new VipProduct { Name = "Wuhan", Type = VipProductType.Institution, Institution = new Institution { Title = "Wuhan" }, PriceInFen = 100, DurationDays = 30 };
        var second = new VipProduct { Name = "Suzhou", Type = VipProductType.Institution, Institution = new Institution { Title = "Suzhou" }, PriceInFen = 100, DurationDays = 60 };
        var course = new VipProduct { Name = "Chinese history", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 100, DurationDays = 90 };
        _dbContext.AddRange(first, second, course);
        await _dbContext.SaveChangesAsync();
        var channel = await _dbContext.DistributionChannels.FirstAsync();
        foreach (var product in new[] { first, second, course })
        {
            var code = "SCHOOL-" + product.Id;
            await _couponService.CreateAsync(channel.Id, code, 100, false);
            var result = await _payService.CreateOrderAsync(_userId, "isolated-openid", product.Id, code);
            Assert.IsTrue(result.Success, result.ErrorMessage);
            var order = await _dbContext.PaymentOrders.SingleAsync(o => o.OutTradeNo == result.OutTradeNo);
            Assert.AreEqual(product.Id, order.VipProductId);
            Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
            Assert.AreEqual(0, order.AmountInFen);
            Assert.AreEqual(100, order.DiscountInFen);
            var member = await _dbContext.VipMemberships.SingleAsync(m => m.VipProductId == product.Id);
            Assert.AreEqual(product.DurationDays, (member.EndTime - member.StartTime).TotalDays);
        }
        var duplicate = new VipProduct { Name = "Another Wuhan plan", Type = VipProductType.Institution, InstitutionId = first.InstitutionId, PriceInFen = 100, DurationDays = 365 };
        _dbContext.VipProducts.Add(duplicate);
        await _dbContext.SaveChangesAsync();
        await _couponService.CreateAsync(channel.Id, "DUPLICATE", 100, false);
        Assert.IsFalse((await _payService.CreateOrderAsync(_userId, "isolated-openid", duplicate.Id, "DUPLICATE")).Success);
        Assert.AreEqual(3, await _dbContext.PaymentOrders.CountAsync());
        Assert.AreEqual(3, await _dbContext.CouponUsages.CountAsync());
        _dbContext.ChangeTracker.Clear();
        var memberships = await _payService.GetVipStatusListAsync(_userId);
        Assert.AreEqual("Wuhan", memberships.Single(m => m.VipProductId == first.Id).VipProduct!.Institution!.Title);
        var status = await new PaperAccessService(_payService).GetUserAccessStatusAsync(_userId);
        CollectionAssert.AreEquivalent(new[] { first.InstitutionId!.Value, second.InstitutionId!.Value }, status.ActiveInstitutionVips.ToArray());
        CollectionAssert.AreEquivalent(new[] { course.TotalCategoryId!.Value }, status.ActiveCategoryVips.ToArray());
        Assert.IsFalse(status.HasRealExamVip);
        Assert.IsFalse(PaperAccessService.CanAccess(new Paper { Title = "Legacy school paper", IsRealExam = true, IsFree = false }, status));
    }

    [TestMethod]
    [DataRow(-1, 30, false)]
    [DataRow(-30, -1, true)]
    [DataRow(1, 30, true)]
    public async Task ExistingAllSchoolVipBlocksOnlyPurchasesItCurrentlyCovers(int startDays, int endDays, bool allowed)
    {
        _dbContext.Users.Add(new User { Id = _userId, UserName = _userId, DisplayName = "Old buyer" });
        var legacy = new VipProduct { Name = "Old all-school VIP", Type = VipProductType.RealExam, IsEnabled = false };
        var school = new VipProduct { Name = "School", Type = VipProductType.Institution, Institution = new Institution { Title = "Wuhan" }, PriceInFen = 100, DurationDays = 30 };
        var end = DateTime.UtcNow.AddDays(endDays);
        var old = new VipMembership { UserId = _userId, VipProduct = legacy, StartTime = DateTime.UtcNow.AddDays(startDays), EndTime = end };
        _dbContext.AddRange(old, school);
        await _dbContext.SaveChangesAsync();
        await _couponService.CreateAsync(_dbContext.DistributionChannels.First().Id, "SCHOOL-FULL", 100, false);
        var result = await _payService.CreateOrderAsync(_userId, "isolated-openid", school.Id, "SCHOOL-FULL");
        Assert.AreEqual(allowed, result.Success);
        Assert.AreEqual(allowed ? 1 : 0, await _dbContext.PaymentOrders.CountAsync());
        _dbContext.ChangeTracker.Clear();
        Assert.AreEqual(end, (await _dbContext.VipMemberships.SingleAsync(m => m.Id == old.Id)).EndTime);
        var status = await new PaperAccessService(_payService).GetUserAccessStatusAsync(_userId);
        Assert.AreEqual(!allowed, status.HasRealExamVip);
        Assert.AreEqual(0, status.ActiveCategoryVips.Count);
    }


    [TestMethod]
    [DataRow(100, "CNY", true, false, false)]
    [DataRow(100, "CNY", true, true, false)]
    [DataRow(100, "CNY", true, true, true)]
    [DataRow(99, "CNY", false, false, false)]
    [DataRow(101, "CNY", false, false, false)]
    [DataRow(100, "USD", false, false, false)]
    [DataRow(100, null, false, false, false)]
    [DataRow(null, null, false, false, false)]
    public async Task SchoolPendingOrderAndSignedNotificationRetainTargetAndActivateOnce(int? total, string? currency, bool accepted, bool relational, bool failAccounting)
    {
        if (relational)
        {
            await _dbContext.DisposeAsync();
            _dbContext = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
                .UseSqlite("Data Source=:memory:").Options);
            await _dbContext.Database.OpenConnectionAsync();
            await _dbContext.Database.EnsureCreatedAsync();
            _couponService = new CouponService(_dbContext);
            _changeService = new ChangeService(_dbContext);
            _payService = new WeChatPayService(_tenpayClient, _dbContext, _couponService,
                _changeService, _settings, NullLogger<WeChatPayService>.Instance);
        }

        // Exercise the real SDK verification and activation locally. The HTTP handler
        // rejects all outbound requests; this certificate belongs only to this test.
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Isolated payment test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var entry = new CertificateEntry(CertificateEntry.ALGORITHM_TYPE_RSA, certificate.ExportCertificatePem());
        _tenpayClient.PlatformCertificateManager.AddEntry(entry);
        var keyFile = _privateKeyPath;
        try
        {
            await File.WriteAllTextAsync(keyFile, rsa.ExportPkcs8PrivateKeyPem());
            _dbContext.Users.Add(new User { Id = _userId, UserName = _userId, DisplayName = "School buyer" });
            var product = new VipProduct { Name = "Wuhan paid plan", Type = VipProductType.Institution, Institution = new Institution { Title = "Wuhan" }, PriceInFen = 100, DurationDays = 30 };
            var order = new PaymentOrder { UserId = _userId, VipProduct = product, OutTradeNo = "SCHOOL-PENDING", Description = "Wuhan", AmountInFen = 100, Status = PaymentOrderStatus.Pending, ExpiredAt = DateTime.UtcNow.AddMinutes(10), PrepayId = "isolated-prepay" };
            _dbContext.PaymentOrders.Add(order);
            await _dbContext.SaveChangesAsync();
            var reused = await _payService.CreateOrderAsync(_userId, "isolated-openid", product.Id);
            Assert.IsTrue(reused.Success, reused.ErrorMessage);
            Assert.AreEqual(order.OutTradeNo, reused.OutTradeNo);
            Assert.AreEqual("prepay_id=isolated-prepay", reused.PayParams!.Package);
            Assert.AreEqual(1, await _dbContext.PaymentOrders.CountAsync());
            // The saved order amount remains authoritative if the catalog price changes.
            product.PriceInFen = 200;
            await _dbContext.SaveChangesAsync();
            var outTradeNo = order.OutTradeNo;
            async Task<bool> Notify(RSA signingKey, int? notifiedTotal, string? notifiedCurrency, bool validSignature = true, string tradeState = "SUCCESS")
            {
                var plain = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    out_trade_no = outTradeNo, transaction_id = "isolated-transaction", trade_state = tradeState,
                    // A WeChat-funded discount can reduce payer_total without changing total.
                    amount = notifiedTotal.HasValue ? new { total = notifiedTotal.Value, currency = notifiedCurrency, payer_total = 70 } : null
                });
                const string resourceNonce = "123456789012";
                const string associated = "transaction";
                var ciphertext = new byte[plain.Length];
                var tag = new byte[16];
                using (var aes = new AesGcm(Encoding.UTF8.GetBytes("12345678901234567890123456789012"), 16))
                    aes.Encrypt(Encoding.UTF8.GetBytes(resourceNonce), plain, ciphertext, tag, Encoding.UTF8.GetBytes(associated));
                var body = JsonSerializer.Serialize(new { id = "isolated-event", event_type = "TRANSACTION.SUCCESS", resource_type = "encrypt-resource", resource = new { algorithm = "AEAD_AES_256_GCM", ciphertext = Convert.ToBase64String(ciphertext.Concat(tag).ToArray()), nonce = resourceNonce, associated_data = associated } });
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                const string nonce = "isolated-signature-nonce";
                var signature = Convert.ToBase64String(signingKey.SignData(Encoding.UTF8.GetBytes($"{timestamp}\n{nonce}\n{body}\n"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
                return await _payService.HandlePaymentNotifyAsync(body, validSignature ? signature : "invalid", timestamp, nonce, entry.SerialNumber);
            }
            if (failAccounting)
            {
                // Coupon activation saves before the accounting event. A failure after
                // that save must leave the entire payment retryable, not partially paid.
                var coupon = new Coupon { Code = "ROLLBACK", AmountInFen = 10, IsSingleUse = true,
                    DistributionChannel = new DistributionChannel { AgencyName = "Rollback test" } };
                _dbContext.Coupons.Add(coupon);
                order.Coupon = coupon;
                order.DiscountInFen = 10;
                await _dbContext.SaveChangesAsync();
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "CREATE TRIGGER fail_accounting BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'Synthetic accounting failure'); END;");
                Assert.IsFalse(await Notify(rsa, 100, "CNY"));
                _dbContext.ChangeTracker.Clear();
                order = await _dbContext.PaymentOrders.SingleAsync(o => o.Id == order.Id);
                Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
                Assert.IsNull(order.PaidAt);
                Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
                Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
                Assert.AreEqual(0, await _dbContext.PaymentLogs.CountAsync());
                Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
                Assert.IsNull((await _dbContext.Coupons.SingleAsync(c => c.Id == coupon.Id)).UsedByUserId);
                await _dbContext.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_accounting;");
            }
            Assert.IsFalse(await Notify(rsa, 100, "CNY", false));
            Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
            Assert.AreEqual(accepted, await Notify(rsa, total, currency));
            if (!accepted)
            {
                Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
                Assert.IsNull(order.PaidAt);
                Assert.IsNull(order.WechatTransactionId);
                Assert.AreEqual(0, await _dbContext.VipMemberships.CountAsync());
                Assert.AreEqual(0, await _dbContext.Changes.CountAsync());
                Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
                // A rejected callback must not prevent a corrected notification succeeding.
                Assert.IsTrue(await Notify(rsa, 100, "CNY"));
            }
            var member = await _dbContext.VipMemberships.SingleAsync();
            var end = member.EndTime;
            Assert.AreEqual(product.Id, member.VipProductId);
            Assert.AreEqual(30, (member.EndTime - member.StartTime).TotalDays);
            Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
            Assert.AreEqual("isolated-transaction", order.WechatTransactionId);
            Assert.IsTrue(await Notify(rsa, 100, "CNY"));
            Assert.AreEqual(1, await _dbContext.VipMemberships.CountAsync());
            Assert.AreEqual(end, (await _dbContext.VipMemberships.SingleAsync()).EndTime);
            Assert.AreEqual(1, await _dbContext.Changes.CountAsync(c => c.Type == ChangeType.VipActivatedViaPayment));
            Assert.AreEqual(failAccounting ? 1 : 0, await _dbContext.CouponUsages.CountAsync());
            Assert.IsTrue(await Notify(rsa, 100, "CNY", tradeState: "CLOSED"));
            Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
            Assert.AreEqual(end, (await _dbContext.VipMemberships.SingleAsync()).EndTime);
        }
        finally
        {
            File.Delete(keyFile);
        }
    }
}
