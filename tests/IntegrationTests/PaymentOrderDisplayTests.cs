using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Resources;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaymentOrderDisplayTests : TestBase
{
    private string _directory = null!;

    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("OrderDisplay-").FullName;
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

    private async Task<(Guid Order, string Buyer, Guid Product)> Seed(PaymentOrderStatus status = PaymentOrderStatus.Paid,
        PaymentChannel channel = PaymentChannel.WeChatVirtual, string? membershipState = null)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = new User { DisplayName = "Student <One>", UserName = Guid.NewGuid().ToString("N") };
        var product = new VipProduct { Name = "Changed catalog price", PriceInFen = 49900, DurationDays = 366,
            TotalCategory = new TotalCategory { Title = "Course" } };
        var coupon = new Coupon { Code = "Code <only>", AmountInFen = 99999,
            DistributionChannel = new DistributionChannel { AgencyName = "Agency" } };
        var order = new PaymentOrder { UserId = user.Id, User = user, VipProduct = product, Coupon = coupon,
            OutTradeNo = "DISPLAY-" + Guid.NewGuid().ToString("N")[..20], Description = "Historical purchase",
            AmountInFen = 200, DiscountInFen = 19600, Status = status, Channel = channel,
            PaidAt = status is PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded
                ? new DateTime(2026, 11, 13, 19, 29, 26, DateTimeKind.Utc) : null };
        db.Add(order);
        if (membershipState != null)
        {
            var now = DateTime.UtcNow;
            // A current membership may come from a later purchase or manual grant.
            db.VipMemberships.Add(new VipMembership { UserId = user.Id, VipProduct = product,
                StartTime = membershipState == "future" ? now.AddDays(1) : now.AddDays(-10),
                EndTime = membershipState == "expired" ? now.AddDays(-1) : now.AddDays(30) });
        }
        await db.SaveChangesAsync();
        return (order.Id, user.Id, product.Id);
    }

    [TestMethod]
    public async Task AllOrderScreensUseSavedAmountsAndEncodeNamesWithoutFinancialChanges()
    {
        var item = await Seed();
        await LoginAsAdmin();
        foreach (var url in new[] { "/PaymentOrders", $"/PaymentOrders/Details/{item.Order}", $"/PaymentOrders/UserPayments?userId={item.Buyer}" })
        {
            var raw = await Http.GetStringAsync(url);
            var html = WebUtility.HtmlDecode(raw);
            foreach (var amount in new[] { "¥198.00", "¥196.00", "¥2.00" }) Assert.Contains(amount, html);
            Assert.DoesNotContain("¥499.00", html);
            Assert.DoesNotContain("¥999.99", html);
            Assert.Contains("WeChat virtual payment", html);
            Assert.Contains("Payment confirmed", html);
            Assert.Contains("Student &lt;One&gt;", raw);
            Assert.Contains("data-utc-time=", html);
        }
        var details = await Http.GetStringAsync($"/PaymentOrders/Details/{item.Order}");
        Assert.Contains("Code &lt;only&gt;", details);
        Assert.Contains("No membership record for this product", details);
        Assert.Contains("<details class=\"card\">", details);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var saved = await db.PaymentOrders.SingleAsync(o => o.Id == item.Order);
        Assert.AreEqual(200, saved.AmountInFen);
        Assert.AreEqual(19600, saved.DiscountInFen);
        Assert.AreEqual(PaymentOrderStatus.Paid, saved.Status);
        Assert.AreEqual(0, await db.PaymentLogs.CountAsync());
        Assert.AreEqual(0, await db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await db.VipMemberships.CountAsync());
    }

    [TestMethod]
    [DataRow("active", "Active")]
    [DataRow("expired", "Expired")]
    [DataRow("future", "Not started")]
    public async Task HistoricalPaymentIsSeparateFromCurrentMembership(string state, string expected)
    {
        var item = await Seed(membershipState: state);
        await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/PaymentOrders/Details/{item.Order}"));
        Assert.Contains("Payment confirmed", html);
        Assert.Contains("Current membership", html);
        Assert.Contains(expected, html);
        Assert.Contains("does not mean the membership is still active today", html);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var membership = await db.VipMemberships.SingleAsync(v => v.UserId == item.Buyer && v.VipProductId == item.Product);
        Assert.IsNull(membership.LastPaymentOrderId);
        Assert.AreEqual(state == "active", membership.IsActive);
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Pending, "Awaiting payment confirmation")]
    [DataRow(PaymentOrderStatus.Failed, "Payment needs verification")]
    [DataRow(PaymentOrderStatus.Paid, "Payment confirmed")]
    [DataRow(PaymentOrderStatus.Closed, "Closed")]
    [DataRow(PaymentOrderStatus.Refunded, "Refunded")]
    public async Task StatusLabelsKeepExistingFilterValuesAndGatewayMeaning(PaymentOrderStatus status, string label)
    {
        var item = await Seed(status, PaymentChannel.WeChatJsApi);
        await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/PaymentOrders?status={status}&userId={item.Buyer}"));
        Assert.Contains($"value=\"{status}\" selected=\"selected\"", html);
        Assert.Contains("WeChat Pay", html);
        Assert.Contains(label, html);
        Assert.Contains("An unconfirmed order is not a completed purchase", html);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(status, (await db.PaymentOrders.SingleAsync(o => o.Id == item.Order)).Status);
    }

    [TestMethod]
    public async Task PaymentReadPermissionAllowsInspectionWithoutGrantingMembershipManagement()
    {
        var item = await Seed();
        var url = $"/PaymentOrders/Details/{item.Order}";
        Assert.AreNotEqual(HttpStatusCode.OK, (await Http.GetAsync(url)).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        Assert.AreNotEqual(HttpStatusCode.OK, (await Http.GetAsync(url)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!,
                new Claim(AppPermissions.Type, AppPermissionNames.CanReadPaymentOrders));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync(url)).StatusCode);
        Assert.AreNotEqual(HttpStatusCode.OK, (await Http.GetAsync("/VipMembers")).StatusCode);
    }

    [TestMethod]
    public async Task PastedOrderNumberFiltersBothRowsAndCountWithoutChangingTheOrder()
    {
        var item = await Seed();
        await Seed(PaymentOrderStatus.Pending);
        string number;
        using (var scope = Server!.Services.CreateScope())
            number = (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().PaymentOrders.SingleAsync(o => o.Id == item.Order)).OutTradeNo;
        await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync("/PaymentOrders?orderNumber=" + Uri.EscapeDataString("  " + number + "  ")));
        Assert.Contains("Orders (1)", html);
        Assert.Contains(number, html);
        Assert.DoesNotContain("bg-warning text-dark", html);
        var missing = WebUtility.HtmlDecode(await Http.GetStringAsync("/PaymentOrders?orderNumber=missing-order"));
        Assert.Contains("Orders (0)", missing);
    }

    [TestMethod]
    public async Task ChinesePageResolvesGeneratedPartialResources()
    {
        var item = await Seed(membershipState: "active");
        await LoginAsAdmin();
        var url = $"/PaymentOrders/Details/{item.Order}";
        await Http.GetAsync("/Culture/Set?culture=zh-CN&returnUrl=" + Uri.EscapeDataString(url));
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync(url));
        foreach (var (scope, key) in new[] { ("_OrderAmounts", "Original price"), ("_PaymentStatus", "Payment confirmed"),
                     ("_PaymentChannel", "WeChat virtual payment"), ("_MembershipStatus", "Active"), ("Details", "Current membership") })
        {
            var resources = new ResourceManager("Aiursoft.WeChatExam.Resources.Views.PaymentOrders." + scope, typeof(Startup).Assembly);
            var translated = resources.GetString(key, CultureInfo.GetCultureInfo("zh-CN"));
            Assert.IsNotNull(translated);
            Assert.AreNotEqual(key, translated);
            Assert.Contains(translated, html);
        }
    }
}
