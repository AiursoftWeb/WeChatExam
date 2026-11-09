using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Aiursoft.WeChatExam.Configuration;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class VirtualPaymentApiTests
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private IHost? _server;
    private string _database = null!;
    private readonly Guid _productId = Guid.NewGuid();
    private readonly ApiHandler _handler = new();
    private readonly StubWeChatService _login = new();
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _database = Path.Combine(Path.GetTempPath(), $"virtual-api-{Guid.NewGuid():N}.db");
        TestStartupWithMockWeChat.SqliteConnectionString = $"Data Source={_database}";
        TestStartupWithMockWeChat.VirtualPaymentHttpClientFactory = new ApiFactory(_handler);
        TestStartupWithMockWeChat.MockWeChatService = _login;
        TestStartupWithMockWeChat.MockWeChatPayService = new StubWeChatPayService();
        TestStartupWithMockWeChat.MockDistributionChannelService = new StubDistributionChannelService();
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<TestStartupWithMockWeChat>([
            "--AppSettings:WeChatEnabled=true", "--AppSettings:WeChat:AppId=test-app", "--AppSettings:WeChat:AppSecret=synthetic-app-secret-with-at-least-32-characters",
            "--AppSettings:WeChat:VirtualPayment:Enabled=" + (TestContext.TestName != nameof(DisabledSalesRejectNewCheckoutButRecoverExistingPurchases)), "--AppSettings:WeChat:VirtualPayment:Environment=1",
            "--AppSettings:WeChat:VirtualPayment:OfferId=offer", "--AppSettings:WeChat:VirtualPayment:SandboxAppKey=sandbox-key",
            $"--AppSettings:WeChat:VirtualPayment:Products:{_productId}:ProductId=course",
            $"--AppSettings:WeChat:VirtualPayment:Products:{_productId}:PriceInFen=19800"
        ], port: reservation.Port);
        reservation.ConfigureServer(_server);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.SeedAsync();
        using (var scope = _server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Add(new VipProduct { Id = _productId, Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 });
            await db.SaveChangesAsync();
        }
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_server != null) { await _server.StopAsync(); _server.Dispose(); }
        _http.Dispose(); _handler.Dispose();
        TestStartupWithMockWeChat.SqliteConnectionString = null;
        TestStartupWithMockWeChat.VirtualPaymentHttpClientFactory = null;
        TestStartupWithMockWeChat.MockWeChatService = null;
        TestStartupWithMockWeChat.MockWeChatPayService = null;
        TestStartupWithMockWeChat.MockDistributionChannelService = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_database)) File.Delete(_database);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AnonymousRequestsCannotUseEitherNewOrOldPaymentRoutes(bool invalidToken)
    {
        if (invalidToken) _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using var create = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/unknown-order");
        using var lookup = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={_productId}");
        Assert.AreEqual(HttpStatusCode.Unauthorized, lookup.StatusCode);
        using var legacy = await _http.PostAsJsonAsync("/api/payment/create-order", new { vipProductId = _productId });
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, query.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, legacy.StatusCode);
        Assert.AreEqual(0, _handler.Calls);
    }

    [TestMethod]
    public async Task HttpCheckoutReturnsVirtualStringsAndNeverAcceptsClientPrices()
    {
        await SignIn();
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", new
        { vipProductId = _productId, loginCode = "fresh-code", amountInFen = 1, discountInFen = 19799, openId = "attacker", status = "Paid" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.IsTrue(root.GetProperty("success").GetBoolean());
        Assert.AreEqual("Pending", root.GetProperty("status").GetString());
        Assert.AreEqual(19800, root.GetProperty("amountInFen").GetInt32());
        Assert.AreEqual(19800, root.GetProperty("originalAmountInFen").GetInt32());
        Assert.AreEqual(0, root.GetProperty("discountInFen").GetInt32());
        var pay = root.GetProperty("payParams");
        Assert.AreEqual("short_series_goods", pay.GetProperty("mode").GetString());
        Assert.AreEqual(JsonValueKind.String, pay.GetProperty("signData").ValueKind);
        Assert.AreEqual(64, pay.GetProperty("paySig").GetString()!.Length);
        Assert.AreEqual(64, pay.GetProperty("signature").GetString()!.Length);
        Assert.IsFalse(pay.TryGetProperty("package", out _));
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        using var scope = _server!.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(pay.GetProperty("signData").GetString(), (await db.VirtualPaymentSnapshots.SingleAsync()).SignData);
        Assert.AreEqual(0, await db.VipMemberships.CountAsync());
        Assert.AreEqual(1, _handler.Calls);
    }

    [TestMethod]
    [DataRow("missing-code")]
    [DataRow("oversized-code")]
    [DataRow("invalid-guid")]
    public async Task InvalidModelsAreRejectedBeforeCodeExchange(string fault)
    {
        await SignIn();
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", new
        { vipProductId = fault == "invalid-guid" ? "invalid" : _productId.ToString(), loginCode = fault == "missing-code" ? "" : fault == "oversized-code" ? new string('x', 513) : "fresh-code" });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); Assert.AreEqual(0, _handler.Calls);
    }

    [TestMethod]
    public async Task UnavailableInfrastructureNeverLeaksItsExceptionOrCreatesAnOrder()
    {
        await SignIn();
        using (var scope = _server!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_api BEFORE INSERT ON VirtualPaymentSnapshots BEGIN SELECT RAISE(ABORT, 'sensitive-database-details'); END;");
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("sensitive-database-details", body);
        Assert.Contains("virtual_payment_unavailable", body);
        using var check = _server.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(0, await db.PaymentOrders.CountAsync()); Assert.AreEqual(0, await db.VirtualPaymentSnapshots.CountAsync());
    }

    [TestMethod]
    public async Task WrongFreshWechatAccountGetsBusinessFeedbackWithoutGrantingRights()
    {
        await SignIn(); _handler.OpenId = "different-account";
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sign in again", await response.Content.ReadAsStringAsync());
        using var scope = _server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().PaymentOrders.CountAsync());
    }

    [TestMethod]
    public async Task CouponRejectionReturnsBusinessFeedbackWithoutReservingAnything()
    {
        await SignIn();
        string? expected;
        using (var scope = _server!.Services.CreateScope())
            expected = (await scope.ServiceProvider.GetRequiredService<ICouponService>().ValidateCouponAsync("not-a-coupon", _productId, "buyer")).ErrorMessage;
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("not-a-coupon"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(expected, body.RootElement.GetProperty("error").GetString());
        using var check = _server.Services.CreateScope();
        Assert.AreEqual(0, await check.ServiceProvider.GetRequiredService<WeChatExamDbContext>().PaymentOrders.CountAsync());
    }

    [TestMethod]
    public async Task IncompleteProductConfigurationReturnsUnavailableWithoutLeakingOrReserving()
    {
        await SignIn();
        _server!.Services.GetRequiredService<IOptions<AppSettings>>().Value.WeChat.VirtualPayment.Products.Clear();
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("virtual_payment_unavailable", body); Assert.DoesNotContain("sandbox-key", body);
        using var scope = _server.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().PaymentOrders.CountAsync());
    }

    [TestMethod]
    public async Task ServerVerifiedQuerySettlesExactlyOnceAndReturnsAnOwnedStatus()
    {
        await SignIn(); var number = await CreatePurchase();
        _handler.Order = number;
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/" + number);
        Assert.AreEqual(HttpStatusCode.OK, query.StatusCode);
        var status = await query.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>();
        Assert.AreEqual("Paid", status!.Status); Assert.IsNotNull(status.PaidAt); Assert.AreEqual(19800, status.AmountInFen);
        using var again = await _http.GetAsync("/api/virtual-payment/order-status/" + number);
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(1, _handler.Queries);
        using var scope = _server!.Services.CreateScope();var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(1, await db.VipMemberships.CountAsync()); Assert.AreEqual(1, await db.Changes.CountAsync());
        Assert.IsTrue(query.Headers.CacheControl!.NoStore);
    }

    [TestMethod]
    [DataRow("other-owner")]
    [DataRow("ordinary-channel")]
    [DataRow("missing")]
    public async Task QueryAuthorizesOwnershipAndChannelBeforeAnyServerRequest(string fault)
    {
        await SignIn(); var number = await CreatePurchase();
        if (fault == "other-owner") await SignIn("another-buyer");
        if (fault == "ordinary-channel")
        {
            using var scope = _server!.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
        }
        var calls = _handler.Calls;
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/" + (fault == "missing" ? "missing-order" : number));
        Assert.AreEqual(HttpStatusCode.NotFound, query.StatusCode); Assert.AreEqual(calls, _handler.Calls);
    }

    [TestMethod]
    public async Task AnUnverifiedGatewayResponseDoesNotTurnIntoClientPaymentSuccess()
    {
        await SignIn(); var number = await CreatePurchase(); _handler.Order = number; _handler.OrderFee = 1;
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/" + number);
        Assert.AreEqual(HttpStatusCode.OK, query.StatusCode);
        Assert.AreEqual("Pending", (await query.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>())!.Status);
        using var scope = _server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().VipMemberships.CountAsync());
    }

    [TestMethod]
    public async Task ZeroAmountResponseIsPaidWithoutAnyGatewayParametersOrOrderQuery()
    {
        await SignIn();
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Add(new Coupon { Code = "FREE", AmountInFen = 19800, IsSingleUse = false,
                DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } }); await db.SaveChangesAsync();
        }
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("FREE"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateVirtualPaymentOrderResponse>();
        Assert.AreEqual("Paid", result!.Status); Assert.IsNull(result.PayParams); Assert.AreEqual(0, result.AmountInFen);
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/" + result.OutTradeNo);
        Assert.AreEqual("Paid", (await query.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>())!.Status);
        Assert.AreEqual(0, _handler.Queries);
        var orders = await FindOrders();
        Assert.AreEqual(result.OutTradeNo, orders.Items.Single().OutTradeNo);
        Assert.AreEqual("Paid", orders.Items.Single().Status);
        Assert.AreEqual(0, orders.Items.Single().AmountInFen);
        Assert.IsNotNull(orders.Items.Single().PaidAt);
    }

    [TestMethod]
    public async Task DisabledSalesRejectNewCheckoutButRecoverExistingPurchases()
    {
        await SignIn();
        using var create = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        Assert.Contains("virtual_payment_disabled", await create.Content.ReadAsStringAsync());
        Assert.AreEqual(0, _handler.Calls);
        const string number = "SAVED-VIRTUAL-ORDER";
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var buyer = await db.Users.SingleAsync(u => u.MiniProgramOpenId == "buyer-openid");
            var order = new PaymentOrder { UserId = buyer.Id, VipProductId = _productId, OutTradeNo = number,
                Channel = PaymentChannel.WeChatVirtual, Description = "Past purchase", AmountInFen = 19800 };
            db.Add(order);
            db.Add(new VirtualPaymentSnapshot { PaymentOrderId = order.Id, AppId = "test-app", OpenId = "buyer-openid",
                SignData = JsonSerializer.Serialize(new { offerId = "offer", buyQuantity = 1, env = 1, currencyType = "CNY",
                    productId = "course", goodsPrice = 19800, outTradeNo = number, attach = order.Id.ToString("N") }) });
            await db.SaveChangesAsync();
        }
        // Recovery discovery does not require a currently configured product.
        var settings = _server!.Services.GetRequiredService<IOptions<AppSettings>>().Value.WeChat.VirtualPayment;
        settings.Products.Clear();
        var orders = await FindOrders();
        Assert.AreEqual(number, orders.Items.Single().OutTradeNo);
        Assert.AreEqual(0, _handler.Calls);
        _handler.Order = number;
        using var query = await _http.GetAsync("/api/virtual-payment/order-status/" + number);
        Assert.AreEqual(HttpStatusCode.OK, query.StatusCode);
        Assert.AreEqual("Paid", (await query.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>())!.Status);
        Assert.AreEqual(1, _handler.Queries);
    }

    [TestMethod]
    public async Task ServerConfirmedClosureAllowsAFreshNumberAndTheSameUnusedCoupon()
    {
        await SignIn();
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Add(new Coupon { Code = "RESERVED", AmountInFen = 1000, IsSingleUse = true,
                DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } });
            await db.SaveChangesAsync();
        }
        using var initial = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("RESERVED"));
        Assert.AreEqual(HttpStatusCode.OK, initial.StatusCode);
        var first = (await initial.Content.ReadFromJsonAsync<CreateVirtualPaymentOrderResponse>())!;
        _handler.Order = first.OutTradeNo; _handler.OrderStatus = 6; _handler.OrderFee = 18800;
        using var closed = await _http.GetAsync("/api/virtual-payment/order-status/" + first.OutTradeNo);
        Assert.AreEqual("Closed", (await closed.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>())!.Status);
        using var next = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("RESERVED"));
        Assert.AreEqual(HttpStatusCode.OK, next.StatusCode);
        var second = (await next.Content.ReadFromJsonAsync<CreateVirtualPaymentOrderResponse>())!;
        Assert.AreNotEqual(first.OutTradeNo, second.OutTradeNo);
        Assert.AreEqual(18800, second.AmountInFen); Assert.AreEqual("RESERVED", second.AppliedCouponCode);
        using var check = _server.Services.CreateScope(); var context = check.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(2, await context.PaymentOrders.CountAsync()); Assert.AreEqual(0, await context.CouponUsages.CountAsync());
        Assert.AreEqual(0, await context.VipMemberships.CountAsync());
        Assert.IsNull((await context.Coupons.SingleAsync(c => c.Code == "RESERVED")).UsedByUserId);
    }

    [TestMethod]
    public async Task LostCheckoutResponseIsDiscoverableWithoutSigningOrReleasingItsCoupon()
    {
        await SignIn();
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Add(new Coupon { Code = "LOST", AmountInFen = 1000, IsSingleUse = true,
                DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } });
            await db.SaveChangesAsync();
        }
        // Deliberately discard the successful response body: the phone has no number.
        using (var lost = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("LOST")))
            Assert.AreEqual(HttpStatusCode.OK, lost.StatusCode);
        var calls = _handler.Calls;
        using var response = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={_productId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "payParams", "signData", "paySig", "signature", "session", "buyer-openid", "sandbox-key" })
            Assert.DoesNotContain(secret, body);
        var orders = JsonSerializer.Deserialize<VirtualPaymentOrderPage>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var original = orders.Items.Single();
        Assert.AreEqual(1, orders.Total); Assert.AreEqual("Pending", original.Status);
        Assert.AreEqual(19800, original.OriginalAmountInFen); Assert.AreEqual(1000, original.DiscountInFen);
        Assert.AreEqual(18800, original.AmountInFen); Assert.AreEqual(calls, _handler.Calls);
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(PaymentOrderStatus.Pending, (await db.PaymentOrders.SingleAsync()).Status);
            Assert.AreEqual(0, await db.VipMemberships.CountAsync()); Assert.AreEqual(0, await db.CouponUsages.CountAsync());
            Assert.IsNull((await db.Coupons.SingleAsync(c => c.Code == "LOST")).UsedByUserId);
        }
        using var duplicate = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase("LOST"));
        Assert.AreEqual(HttpStatusCode.BadRequest, duplicate.StatusCode);
        _handler.Order = original.OutTradeNo; _handler.OrderFee = 18800;
        using var confirmed = await _http.GetAsync("/api/virtual-payment/order-status/" + original.OutTradeNo);
        Assert.AreEqual("Paid", (await confirmed.Content.ReadFromJsonAsync<PaymentOrderStatusResponse>())!.Status);
        Assert.AreEqual("Paid", (await FindOrders()).Items.Single().Status);
    }

    [TestMethod]
    public async Task DiscoveryIncludesAllLocalStatesAndExpiredReservationsButOnlyOwnedVirtualProductOrders()
    {
        await SignIn("other-buyer"); await SignIn();
        var instant = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var buyer = await db.Users.SingleAsync(u => u.MiniProgramOpenId == "buyer-openid");
            var other = await db.Users.SingleAsync(u => u.MiniProgramOpenId == "other-buyer");
            for (var i = 0; i < 55; i++)
                db.Add(new PaymentOrder { UserId = buyer.Id, VipProductId = _productId, OutTradeNo = $"OWN-{i}",
                    Channel = PaymentChannel.WeChatVirtual, Description = "Saved course price", AmountInFen = 18800,
                    DiscountInFen = 1000, CreatedAt = instant, ExpiredAt = instant.AddMinutes(30), Status = (PaymentOrderStatus)(i % 5) });
            db.Add(new PaymentOrder { UserId = other.Id, VipProductId = _productId, OutTradeNo = "OTHER-OWNER",
                Channel = PaymentChannel.WeChatVirtual, Description = "Other buyer", AmountInFen = 19800 });
            db.Add(new PaymentOrder { UserId = buyer.Id, VipProductId = _productId, OutTradeNo = "ORDINARY",
                Channel = PaymentChannel.WeChatJsApi, Description = "Ordinary purchase", AmountInFen = 19800 });
            db.Add(new PaymentOrder { UserId = buyer.Id, VipProduct = new VipProduct { Name = "Other product", PriceInFen = 100 },
                OutTradeNo = "OTHER-PRODUCT", Channel = PaymentChannel.WeChatVirtual, Description = "Other product", AmountInFen = 100 });
            var product = await db.VipProducts.SingleAsync(p => p.Id == _productId);
            product.IsEnabled = false; product.PriceInFen = 999;
            await db.SaveChangesAsync();
        }
        var first = await FindOrders(count: 50); var last = await FindOrders(offset: 50, count: 50);
        var all = first.Items.Concat(last.Items).ToList();
        Assert.AreEqual(55, first.Total); Assert.AreEqual(55, last.Total);
        Assert.AreEqual(50, first.Items.Count); Assert.AreEqual(5, last.Items.Count);
        Assert.AreEqual(55, all.Select(o => o.OutTradeNo).Distinct().Count());
        CollectionAssert.AreEquivalent(new[] { "Pending", "Paid", "Failed", "Closed", "Refunded" }, all.Select(o => o.Status).Distinct().ToArray());
        Assert.IsTrue(all.All(o => o.OutTradeNo.StartsWith("OWN-", StringComparison.Ordinal) && o.CreatedAt == instant &&
            o.Description == "Saved course price" && o.OriginalAmountInFen == 19800 && o.DiscountInFen == 1000 && o.AmountInFen == 18800));
        CollectionAssert.AreEqual(first.Items.Select(o => o.OutTradeNo).ToArray(), (await FindOrders(count: 50)).Items.Select(o => o.OutTradeNo).ToArray());
        Assert.AreEqual(0, (await FindOrders(offset: int.MaxValue)).Items.Count);
        Assert.AreEqual(0, _handler.Calls);
        using var check = _server!.Services.CreateScope(); var context = check.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(58, await context.PaymentOrders.CountAsync());
        Assert.AreEqual(0, await context.PaymentLogs.CountAsync()); Assert.AreEqual(0, await context.VipMemberships.CountAsync());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("?vipProductId=invalid")]
    [DataRow("?vipProductId=00000000-0000-0000-0000-000000000000")]
    [DataRow("?offset=-1")]
    [DataRow("?count=0")]
    [DataRow("?count=51")]
    [DataRow("?offset=2147483648")]
    public async Task DiscoveryRejectsMissingOrInvalidProductAndPagination(string suffix)
    {
        await SignIn();
        if (suffix.StartsWith("?offset=", StringComparison.Ordinal) || suffix.StartsWith("?count=", StringComparison.Ordinal))
            suffix += $"&vipProductId={_productId}";
        using var response = await _http.GetAsync("/api/virtual-payment/orders" + suffix);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode); Assert.AreEqual(0, _handler.Calls);
    }

    [TestMethod]
    public async Task DiscoveryReturnsAnEmptyPageForAnUnknownProductOrAnotherBuyer()
    {
        await SignIn(); await CreatePurchase(); await SignIn("other-buyer");
        var calls = _handler.Calls;
        Assert.AreEqual(0, (await FindOrders()).Total);
        using var spoofed = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={_productId}&userId=buyer&channel=WeChatJsApi");
        Assert.AreEqual(0, (await spoofed.Content.ReadFromJsonAsync<VirtualPaymentOrderPage>())!.Total);
        using var missing = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.OK, missing.StatusCode);
        Assert.AreEqual(0, (await missing.Content.ReadFromJsonAsync<VirtualPaymentOrderPage>())!.Total);
        Assert.AreEqual(calls, _handler.Calls);
    }

    [TestMethod]
    public async Task DiscoveryDatabaseFailureReturnsSanitizedUnavailableWithoutGatewayRequests()
    {
        await SignIn();
        using (var scope = _server!.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Database.ExecuteSqlRawAsync(
                "ALTER TABLE PaymentOrders RENAME TO SensitiveOrderStorage;");
        using var response = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={_productId}");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("virtual_payment_unavailable", body);
        Assert.DoesNotContain("SQLite", body); Assert.DoesNotContain("PaymentOrders", body);
        Assert.DoesNotContain(_database, body); Assert.AreEqual(0, _handler.Calls);
    }

    private async Task<VirtualPaymentOrderPage> FindOrders(int offset = 0, int count = 20)
    {
        using var response = await _http.GetAsync($"/api/virtual-payment/orders?vipProductId={_productId}&offset={offset}&count={count}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<VirtualPaymentOrderPage>())!;
    }

    private CreateVirtualPaymentOrderRequest Purchase(string? coupon = null) => new() { VipProductId = _productId, LoginCode = "fresh-code", CouponCode = coupon };
    private async Task<string> CreatePurchase()
    {
        using var response = await _http.PostAsJsonAsync("/api/virtual-payment/create-order", Purchase());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateVirtualPaymentOrderResponse>())!.OutTradeNo;
    }
    private async Task SignIn(string openId = "buyer-openid")
    {
        _login.Sessions[openId] = new WeChatSessionResult { IsSuccess = true, OpenId = openId, SessionKey = "old-session" };
        using var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = openId });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<TokenDto>())!.Token);
    }

    private sealed class ApiFactory(ApiHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class ApiHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int Queries { get; private set; }
        public string OpenId { get; set; } = "buyer-openid";
        public string? Order { get; set; }
        public int OrderFee { get; set; } = 19800;
        public int OrderStatus { get; set; } = 2;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var path = request.RequestUri!.AbsolutePath;
            string body;
            if (path == "/sns/jscode2session") body = JsonSerializer.Serialize(new { openid = OpenId, session_key = "fresh-session" });
            else if (path == "/cgi-bin/stable_token") body = "{\"access_token\":\"synthetic-token\",\"expires_in\":7200}";
            else if (path == "/xpay/query_order")
            {
                Queries++; body = JsonSerializer.Serialize(new { errcode = 0, order = new { order_id = Order,
                    env_type = 2, order_type = 0, status = OrderStatus, biz_type = 0, order_fee = OrderFee,
                    paid_fee = OrderStatus == 6 ? 0 : OrderFee, wx_order_id = "platform-order" } });
            }
            else throw new InvalidOperationException("Unexpected virtual-payment endpoint.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
