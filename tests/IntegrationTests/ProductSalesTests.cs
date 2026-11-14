using System.Net;
using System.Security.Claims;
using System.Globalization;
using System.Resources;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ProductSalesTests : TestBase
{
    private string _directory = null!;

    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("ProductSales-").FullName;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>(["--applicationName", "Aiursoft.WeChatExam",
            "--AppSettings:WeChatEnabled=false", $"--Storage:Path={_directory}/storage"], port: reservation.Port);
        reservation.ConfigureServer(Server);
        await Server.UpdateDbAsync<WeChatExamDbContext>();
        await Server.SeedAsync();
        await Server.StartAsync();
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        await base.CleanServer();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task ProductListAndSalesDetailsShowHistoricalReceiptsAndBuyerLinks()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var buyer = await db.Users.FirstAsync();
        buyer.DisplayName = "Buyer <One>";
        var product = new VipProduct { Name = "Sales demo", PriceInFen = 49900, TotalCategory = new TotalCategory { Title = "Course" } };
        var order = new PaymentOrder { UserId = buyer.Id, VipProduct = product, OutTradeNo = "SALES-EXAMPLE",
            Description = "Old purchase", Status = PaymentOrderStatus.Paid, AmountInFen = 200, DiscountInFen = 19600,
            Channel = PaymentChannel.WeChatVirtual, PaidAt = DateTime.UtcNow };
        db.Add(order);
        await db.SaveChangesAsync();
        var index = WebUtility.HtmlDecode(await Http.GetStringAsync("/VipProducts?search=Sales%20demo"));
        Assert.Contains("data-sales=\"paid\">1", index);
        Assert.Contains("data-sales=\"buyers\">1", index);
        Assert.Contains("¥2.00", index);
        Assert.Contains($"/VipProducts/Sales/{product.Id}", index);
        var details = await Http.GetStringAsync($"/VipProducts/Sales/{product.Id}");
        var decoded = WebUtility.HtmlDecode(details);
        Assert.Contains("Actual paid prices", decoded);
        foreach (var amount in new[] { "¥198.00", "¥196.00", "¥2.00" }) Assert.Contains(amount, decoded);
        Assert.DoesNotContain("¥499.00", decoded);
        Assert.Contains("Buyer &lt;One&gt;", details);
        Assert.Contains($"/PaymentOrders/Details/{order.Id}", details);
        Assert.Contains("data-utc-time=", details);
        Assert.Contains("WeChat virtual payment", decoded);
        order.VirtualPaymentSnapshot = new VirtualPaymentSnapshot
        {
            PaymentOrderId = order.Id, AppId = "test-app", OpenId = "test-buyer", SignData = "{}",
            Platform = VirtualPaymentPlatform.Apple
        };
        await db.SaveChangesAsync();
        var verified = await Http.GetStringAsync($"/VipProducts/Sales/{product.Id}");
        Assert.Contains("Apple IAP payment", verified);
        Assert.DoesNotContain("Payment method not yet verified", verified);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/VipProducts/Sales/{Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/VipProducts/Sales/{product.Id}?kind=pending")).StatusCode);
    }

    [TestMethod]
    public async Task OverviewDeduplicatesBuyersAndFollowsProductFilters()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var first = new User { DisplayName = "Repeat buyer" };
        var second = new User { DisplayName = "Other buyer" };
        var freeOnly = new User { DisplayName = "Free or unpaid only" };
        var products = new[] { "Overview A", "Overview B", "Overview Disabled" }
            .Select(name => new VipProduct { Name = name, IsEnabled = name != "Overview Disabled",
                TotalCategory = new TotalCategory { Title = name } }).ToArray();
        db.AddRange(first, second, freeOnly);
        db.VipProducts.AddRange(products);
        PaymentOrder Order(int product, User user, int amount, PaymentOrderStatus status = PaymentOrderStatus.Paid) => new()
        {
            UserId = user.Id, User = user, VipProduct = products[product], Status = status, AmountInFen = amount,
            OutTradeNo = Guid.NewGuid().ToString("N"), Description = "Overview sale"
        };
        db.AddRange(Order(0, first, 200), Order(0, first, 200), Order(1, first, 1000), Order(1, second, 500),
            Order(1, freeOnly, 0), Order(1, freeOnly, 200, PaymentOrderStatus.Refunded),
            Order(1, freeOnly, 99900, PaymentOrderStatus.Pending), Order(2, freeOnly, 3000));
        await db.SaveChangesAsync();
        var index = WebUtility.HtmlDecode(await Http.GetStringAsync("/VipProducts?search=Overview"));
        Assert.Contains("data-overview=\"buyers\">2", index);
        Assert.Contains("data-overview=\"paid\">4", index);
        Assert.Contains("data-overview=\"receipts\">¥19.00", index);
        var filtered = await Http.GetStringAsync("/VipProducts?search=Overview%20A");
        Assert.Contains("data-overview=\"buyers\">1", filtered);
        Assert.DoesNotContain(products[1].Id.ToString(), filtered);
        var disabled = WebUtility.HtmlDecode(await Http.GetStringAsync("/VipProducts?search=Overview&showDisabled=true"));
        Assert.Contains("data-overview=\"buyers\">1", disabled);
        Assert.Contains("data-overview=\"receipts\">¥30.00", disabled);
        var empty = await Http.GetStringAsync("/VipProducts?search=NoSuchProduct");
        Assert.Contains("data-overview=\"buyers\">0", empty);
        Assert.Contains("data-overview=\"paid\">0", empty);
    }

    [TestMethod]
    public async Task DailyTrendKeepsProductScopeAndRangeAcrossOrderGroups()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var buyer = await db.Users.FirstAsync();
        var product = new VipProduct { Name = "Daily trend A", TotalCategory = new TotalCategory { Title = "A" } };
        var other = new VipProduct { Name = "Daily trend B", TotalCategory = new TotalCategory { Title = "B" } };
        PaymentOrder Order(VipProduct item, int amount) => new()
        {
            VipProduct = item, UserId = buyer.Id, Status = PaymentOrderStatus.Paid, AmountInFen = amount,
            PaidAt = DateTime.UtcNow.AddMinutes(-1), OutTradeNo = Guid.NewGuid().ToString("N"), Description = "Daily sale"
        };
        db.PaymentOrders.AddRange(Order(product, 100), Order(other, 500));
        var undated = Order(product, 200);
        undated.PaidAt = null;
        db.PaymentOrders.Add(undated);
        await db.SaveChangesAsync();
        var index = WebUtility.HtmlDecode(await Http.GetStringAsync("/VipProducts?search=Daily%20trend%20A&days=7"));
        Assert.Contains("data-daily-trend", index);
        Assert.Contains("data-trend-total>¥1.00", index);
        Assert.Contains("Historical paid orders without a payment date are excluded from this chart. (1)", index);
        Assert.AreEqual(7, System.Text.RegularExpressions.Regex.Matches(index, "<span data-date=").Count);
        Assert.Contains("name=\"days\" value=\"7\"", index);
        Assert.Contains("days=7", index);
        Assert.Contains("name=\"search\" value=\"Daily trend A\"", index);
        var details = WebUtility.HtmlDecode(await Http.GetStringAsync($"/VipProducts/Sales/{product.Id}?kind=free&days=90"));
        Assert.Contains("data-trend-total>¥1.00", details);
        Assert.AreEqual(90, System.Text.RegularExpressions.Regex.Matches(details, "<span data-date=").Count);
        Assert.Contains("days=90", details);
        Assert.Contains("name=\"kind\" value=\"free\"", details);
        foreach (var url in new[] { "/VipProducts?days=0", "/VipProducts?days=365", $"/VipProducts/Sales/{product.Id}?days=-1" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync(url)).StatusCode);
    }

    [TestMethod]
    public async Task SalesRequireBothProductAndOrderReadPermission()
    {
        var url = $"/VipProducts/Sales/{Guid.NewGuid()}";
        Assert.AreNotEqual(HttpStatusCode.OK, (await Http.GetAsync(url)).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadVipProducts));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var index = await Http.GetStringAsync("/VipProducts");
        Assert.DoesNotContain("Order receipts", index);
        Assert.DoesNotContain("data-sales=", index);
        Assert.DoesNotContain("data-daily-trend", index);
        Assert.AreNotEqual(HttpStatusCode.NotFound, (await Http.GetAsync(url)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadPaymentOrders));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        Assert.Contains("Order receipts", await Http.GetStringAsync("/VipProducts"));
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync(url)).StatusCode);
    }

    [TestMethod]
    public async Task DisabledProductsKeepSalesHistoryAndChineseStatisticsLabels()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var product = new VipProduct { Name = "Stopped sales", IsEnabled = false,
            TotalCategory = new TotalCategory { Title = "Course" } };
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();
        Assert.DoesNotContain("Stopped sales", await Http.GetStringAsync("/VipProducts"));
        Assert.Contains("Stopped sales", await Http.GetStringAsync("/VipProducts?showDisabled=true"));
        Assert.Contains("No confirmed paid purchases yet.", await Http.GetStringAsync($"/VipProducts/Sales/{product.Id}"));
        await Http.GetAsync("/Culture/Set?culture=zh-CN&returnUrl=%2FVipProducts");
        foreach (var (view, url) in new[] { ("Index", "/VipProducts?showDisabled=true"), ("Sales", $"/VipProducts/Sales/{product.Id}") })
        {
            var html = WebUtility.HtmlDecode(await Http.GetStringAsync(url));
            var resources = new ResourceManager("Aiursoft.WeChatExam.Resources.Views.VipProducts." + view, typeof(Startup).Assembly);
            foreach (var key in new[] { "Paid sales", "Order receipts", "Paying buyers", "Free claims" })
            {
                var translated = resources.GetString(key, CultureInfo.GetCultureInfo("zh-CN"));
                Assert.IsNotNull(translated);
                Assert.AreNotEqual(key, translated);
                Assert.Contains(translated, html);
            }
        }
    }

    [TestMethod]
    public async Task PendingRefundReviewsWarnAboutReceiptsWithoutGuessingNetCash()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var buyer = await db.Users.FirstAsync();
        var product = new VipProduct { Name = "Refund review product", TotalCategory = new TotalCategory { Title = "Course" } };
        var order = new PaymentOrder { UserId = buyer.Id, VipProduct = product, OutTradeNo = "REVIEW-SALE",
            Description = "Purchase with an uncertain refund", Status = PaymentOrderStatus.Paid, AmountInFen = 1000 };
        db.Add(order);
        for (var i = 0; i < 2; i++)
            db.PaymentLogs.Add(new PaymentLog { PaymentOrder = order, PaymentOrderId = order.Id,
                EventType = "VirtualPaymentRefundReview", RawData = "{}" });
        await db.SaveChangesAsync();
        var index = WebUtility.HtmlDecode(await Http.GetStringAsync("/VipProducts"));
        Assert.Contains("Refunds need review (1)", index);
        var details = WebUtility.HtmlDecode(await Http.GetStringAsync($"/VipProducts/Sales/{product.Id}"));
        Assert.Contains("These receipts may overstate the amount retained.", details);
        Assert.Contains("/PaymentOrders/RefundReviews", details);
        Assert.Contains("¥10.00", details);
        db.ChangeTracker.Clear();
        Assert.AreEqual(PaymentOrderStatus.Paid, (await db.PaymentOrders.SingleAsync(o => o.Id == order.Id)).Status);
        Assert.AreEqual(2, await db.PaymentLogs.CountAsync(l => l.PaymentOrderId == order.Id));
    }
}
