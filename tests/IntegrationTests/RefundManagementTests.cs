using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class RefundManagementTests : TestBase
{
    private string _directory = null!;
    private readonly List<Guid> _orders = [];
    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("RefundManagement-").FullName;
        using var reservation = new TestPortReservation(); Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>(["--applicationName", "Aiursoft.WeChatExam", "--AppSettings:WeChatEnabled=false", $"--Storage:Path={_directory}/storage"], port: reservation.Port);
        reservation.ConfigureServer(Server); await Server.UpdateDbAsync<WeChatExamDbContext>(); await Server.SeedAsync(); await Server.StartAsync();
    }
    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var orders = await db.PaymentOrders.Where(o => _orders.Contains(o.Id)).ToListAsync();
            var products = orders.Select(o => o.VipProductId).ToList(); var buyers = orders.Select(o => o.UserId).ToList();
            db.VirtualPaymentRefundRequests.RemoveRange(await db.VirtualPaymentRefundRequests.Where(r => _orders.Contains(r.PaymentOrderId)).ToListAsync());
            db.PaymentLogs.RemoveRange(await db.PaymentLogs.Where(l => _orders.Contains(l.PaymentOrderId)).ToListAsync());
            db.VirtualPaymentSnapshots.RemoveRange(await db.VirtualPaymentSnapshots.Where(s => _orders.Contains(s.PaymentOrderId)).ToListAsync());
            db.PaymentOrders.RemoveRange(orders);
            db.VipProducts.RemoveRange(await db.VipProducts.Where(p => products.Contains(p.Id)).ToListAsync());
            db.Users.RemoveRange(await db.Users.Where(u => buyers.Contains(u.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
        await base.CleanServer(); if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<Guid> Seed(VirtualPaymentPlatform? platform, PaymentChannel channel = PaymentChannel.WeChatVirtual, bool pendingRefund = false, bool partial = false)
    {
        using var scope = Server!.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = new User { UserName = Guid.NewGuid().ToString("N"), DisplayName = "Buyer <one>" };
        var order = new PaymentOrder { User = user, UserId = user.Id, OutTradeNo = "WEB-" + Guid.NewGuid().ToString("N")[..20], Description = "Synthetic",
            VipProduct = new VipProduct { Name = "Synthetic course", PriceInFen = 49900, DurationDays = 366 },
            Channel = channel, AmountInFen = 200, DiscountInFen = 19600, Status = PaymentOrderStatus.Paid, PaidAt = DateTime.UtcNow };
        db.Add(order);
        if (channel == PaymentChannel.WeChatVirtual)
            db.Add(new VirtualPaymentSnapshot { PaymentOrderId = order.Id, AppId = "synthetic", OpenId = "synthetic", SignData = "{}", Platform = platform, PlatformOrderId = "synthetic-platform" });
        if (pendingRefund) db.Add(new VirtualPaymentRefundRequest { PaymentOrderId = order.Id, ActorId = "operator", RefundOrderId = "WEB-REFUND-" + order.Id.ToString("N")[..20], AmountInFen = 200 });
        if (partial) db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentRefundReview", RawData = JsonSerializer.Serialize(new {
            confirmation = new { PaymentOrderId = order.Id, PlatformRefundId = "partial-refund", AmountInFen = 100 }, reason = "partial_or_adjusted_refund" }) });
        await db.SaveChangesAsync(); _orders.Add(order.Id); return order.Id;
    }

    [TestMethod]
    [DataRow(VirtualPaymentPlatform.Apple)]
    [DataRow(VirtualPaymentPlatform.WeChat)]
    [DataRow(null)]
    public async Task PaymentMethodControlsTheActionWithoutGuessingFromTheBrowser(VirtualPaymentPlatform? platform)
    {
        var id = await Seed(platform); await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/Refunds/Process/{id}"));
        Assert.Contains("Buyer &lt;one&gt;", await Http.GetStringAsync($"/Refunds/Process/{id}"));
        Assert.Contains("¥2.00", html); Assert.DoesNotContain("¥499.00", html);
        if (platform == VirtualPaymentPlatform.Apple)
        {
            Assert.Contains("Apple IAP payment", html); Assert.Contains("reportaproblem.apple.com", html);
            Assert.Contains("Copy student instructions", html); Assert.DoesNotContain("action=\"/Refunds/Submit\"", html);
        }
        else if (platform == VirtualPaymentPlatform.WeChat)
        {
            Assert.Contains("WeChat virtual payment (non-Apple)", html); Assert.Contains("Confirm full refund ¥2.00", html);
            Assert.Contains("name=\"confirmed\"", html); Assert.Contains("__RequestVerificationToken", html);
        }
        else { Assert.Contains("Verify payment method", html); Assert.DoesNotContain("action=\"/Refunds/Submit\"", html); }
        Assert.Contains("Refund management", await Http.GetStringAsync("/PaymentOrders"));
    }

    [TestMethod]
    public async Task ExistingUncertainIntentIsQueryOnlyAndLegacyOrdersUseTheirMerchantPlatform()
    {
        var pending = await Seed(VirtualPaymentPlatform.WeChat, pendingRefund: true);
        var legacy = await Seed(null, PaymentChannel.WeChatJsApi); await LoginAsAdmin();
        var html = await Http.GetStringAsync($"/Refunds/Process/{pending}");
        Assert.Contains("Check refund result", html); Assert.DoesNotContain("action=\"/Refunds/Submit\"", html); Assert.Contains("data-utc-time=", html);
        html = await Http.GetStringAsync($"/Refunds/Process/{legacy}");
        Assert.Contains("pay.weixin.qq.com", html); Assert.DoesNotContain("action=\"/Refunds/Submit\"", html);
        Assert.Contains("Refund needs verification", await Http.GetStringAsync("/Refunds"));
    }

    [TestMethod]
    public async Task PartialRefundListUsesTheActualRefundAmountAndSeparateRightsReview()
    {
        var id = await Seed(VirtualPaymentPlatform.WeChat, partial: true); await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync("/Refunds"));
        var row = html.Split("<tr>").Single(r => r.Contains($"/Refunds/Process/{id}"));
        Assert.Contains("¥1.00", row); Assert.DoesNotContain("¥2.00", row); Assert.Contains("Needs review", row); Assert.Contains("data-utc-time=", row);
        using var response = await Http.GetAsync("/Refunds?page=-1"); Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task MutationsRequireIndependentPermissionCsrfConfirmationAndPost()
    {
        var id = await Seed(VirtualPaymentPlatform.WeChat);
        var page = $"/Refunds/Process/{id}";
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadPaymentOrders));
        }
        await PostForm("/Account/Login", new Dictionary<string,string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var html = await Http.GetStringAsync(page); Assert.DoesNotContain("action=\"/Refunds/Submit\"", html);
        using var denied = await Http.PostAsync("/Refunds/Submit", new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = id.ToString(), ["amountInFen"] = "200", ["confirmed"] = "true" }));
        Assert.AreNotEqual(HttpStatusCode.OK, denied.StatusCode);
        await LoginAsAdmin();
        using var get = await Http.GetAsync($"/Refunds/Submit?id={id}"); Assert.AreEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var noCsrf = await Http.PostAsync("/Refunds/Submit", new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = id.ToString(), ["confirmed"] = "true", ["amountInFen"] = "200" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var unconfirmed = await PostForm("/Refunds/Submit", new Dictionary<string,string> { ["id"] = id.ToString(), ["amountInFen"] = "200", ["confirmed"] = "false" }, page);
        Assert.AreEqual(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        using var scope2 = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope2.ServiceProvider.GetRequiredService<WeChatExamDbContext>().VirtualPaymentRefundRequests.CountAsync(r => r.PaymentOrderId == id));
    }
}
