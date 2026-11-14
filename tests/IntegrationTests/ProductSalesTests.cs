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
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/VipProducts/Sales/{Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/VipProducts/Sales/{product.Id}?kind=pending")).StatusCode);
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
