using System.Net;
using System.Security.Claims;
using System.Globalization;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class VirtualPaymentReadinessPageTests : TestBase
{
    private string _directory = null!;
    private Guid _productId;
    private const string Url = "/PaymentOrders/VirtualPaymentReadiness";

    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("VirtualReadiness-").FullName;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>(["--applicationName", "Aiursoft.WeChatExam",
            "--AppSettings:WeChatEnabled=false", "--AppSettings:WeChat:AppId=wx1234567890abcdef",
            "--AppSettings:WeChat:AppSecret=synthetic-secret-must-not-appear",
            "--AppSettings:WeChat:VirtualPayment:SandboxAppKey=synthetic-signing-must-not-appear",
            "--AppSettings:WeChat:VirtualPayment:CallbackToken=synthetic-callback-must-not-appear",
            "--AppSettings:WeChat:VirtualPayment:Enabled=false", $"--Storage:Path={_directory}/storage"], port: reservation.Port);
        reservation.ConfigureServer(Server);
        await Server.UpdateDbAsync<WeChatExamDbContext>(); await Server.SeedAsync(); await Server.StartAsync();
        using var scope = Server.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var product = new VipProduct { Name = "Course <script>alert(1)</script>", PriceInFen = 19800, DurationDays = 30,
            TotalCategory = new TotalCategory { Title = "History" } };
        db.Add(product); await db.SaveChangesAsync(); _productId = product.Id;
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        await base.CleanServer();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task AdministratorSeesSafeDiagnosticsAndExistingEditLinkWithoutFinancialWrites()
    {
        await LoginAsAdmin();
        var before = await FinancialCounts();
        using var response = await Http.GetAsync(Url); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        var raw = await response.Content.ReadAsStringAsync(); var html = WebUtility.HtmlDecode(raw);
        Assert.Contains("Virtual payment setup", html); Assert.Contains("New virtual sales disabled", html);
        Assert.Contains("Sandbox", html); Assert.Contains("19800", html); Assert.Contains("data-utc-time=", html);
        Assert.Contains($"/VipProducts/Edit/{_productId}", html);
        Assert.Contains("Passing these checks does not mean sales are ready", html);
        Assert.Contains("Ask the administrator to map", html);
        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        foreach (var secret in new[] { "synthetic-secret-must-not-appear", "synthetic-signing-must-not-appear", "synthetic-callback-must-not-appear" }) Assert.DoesNotContain(secret, html);
        Assert.Contains(Url, await Http.GetStringAsync("/PaymentOrders"));
        CollectionAssert.AreEqual(before, await FinancialCounts());
    }

    private async Task<int[]> FinancialCounts()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        return [await db.PaymentOrders.CountAsync(), await db.PaymentLogs.CountAsync(),
            await db.CouponUsages.CountAsync(), await db.VipMemberships.CountAsync()];
    }

    [TestMethod]
    public async Task AccessRequiresOrderReadPermissionAndDoesNotGrantProductEditing()
    {
        using var anonymous = await Http.GetAsync(Url); Assert.AreNotEqual(HttpStatusCode.OK, anonymous.StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using var denied = await Http.GetAsync(Url); Assert.AreNotEqual(HttpStatusCode.OK, denied.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadPaymentOrders));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        using var allowed = await Http.GetAsync(Url); Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode);
        var html = await allowed.Content.ReadAsStringAsync(); Assert.Contains("View only", html);
        Assert.DoesNotContain($"/VipProducts/Edit/{_productId}", html);
        using var post = await Http.PostAsync(Url, new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [TestMethod]
    public async Task ChinesePageUsesGeneratedTranslationsAndAllViewKeysAreCovered()
    {
        await LoginAsAdmin();
        using var culture = await Http.GetAsync("/Culture/Set?culture=zh-CN&returnUrl=" + Uri.EscapeDataString(Url));
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync(Url));
        var resources = new System.Resources.ResourceManager("Aiursoft.WeChatExam.Resources.Views.PaymentOrders.VirtualPaymentReadiness", typeof(Startup).Assembly);
        using var set = resources.GetResourceSet(CultureInfo.GetCultureInfo("zh-CN"), true, false);
        Assert.IsNotNull(set);
        var title = set.GetString("Virtual payment setup") ?? throw new InvalidOperationException("Missing translated title.");
        Assert.Contains(title, html); Assert.AreNotEqual("Virtual payment setup", title);
        Assert.DoesNotContain("synthetic-secret-must-not-appear", html);
    }
}
