using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class MembershipReadApiTests : TestBase
{
    private string _directory = null!;
    private readonly User _buyer = new() { Id = Guid.NewGuid().ToString(), UserName = "Buyer", DisplayName = "Buyer" };
    private readonly User _other = new() { Id = Guid.NewGuid().ToString(), UserName = "Other", DisplayName = "Other" };
    private readonly Guid _course = Guid.NewGuid(), _paused = Guid.NewGuid(), _school = Guid.NewGuid(), _otherSchool = Guid.NewGuid();
    private readonly Guid _mockVersion = Guid.NewGuid(), _pausedVersion = Guid.NewGuid(), _realVersion = Guid.NewGuid(), _otherVersion = Guid.NewGuid();

    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("MembershipRead-").FullName;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>(["--applicationName", "Aiursoft.WeChatExam",
            "--AppSettings:WeChat:Payment:Enabled=false", "--AppSettings:WeChatEnabled=true",
            "--AppSettings:WeChat:Payment:PrivateKeyFilePath=/missing-key-must-not-be-read.pem",
            $"--ConnectionStrings:DefaultConnection=MembershipRead-{Guid.NewGuid():N}",
            $"--Storage:Path={_directory}/storage"], port: reservation.Port);
        reservation.ConfigureServer(Server);
        await Server.UpdateDbAsync<WeChatExamDbContext>();
        await Server.SeedAsync();
        await Server.StartAsync();
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        try
        {
            if (Server != null)
            {
                using var scope = Server.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                var versions = await db.PaperSnapshots.Where(s => s.Id == _mockVersion || s.Id == _pausedVersion ||
                    s.Id == _realVersion || s.Id == _otherVersion).ToListAsync();
                var mockIds = versions.Where(v => v.MockPaperId.HasValue).Select(v => v.MockPaperId!.Value).ToArray();
                var realIds = versions.Where(v => v.RealPaperId.HasValue).Select(v => v.RealPaperId!.Value).ToArray();
                db.PaperSnapshots.RemoveRange(versions);
                db.MockPapers.RemoveRange(await db.MockPapers.Where(p => mockIds.Contains(p.Id)).ToListAsync());
                db.RealPapers.RemoveRange(await db.RealPapers.Where(p => realIds.Contains(p.Id)).ToListAsync());
                db.VipMemberships.RemoveRange(await db.VipMemberships.Where(m => m.UserId == _buyer.Id || m.UserId == _other.Id).ToListAsync());
                db.VipProducts.RemoveRange(await db.VipProducts.Where(p => p.TotalCategoryId == _course || p.TotalCategoryId == _paused ||
                    p.InstitutionId == _school || p.InstitutionId == _otherSchool).ToListAsync());
                db.TotalCategories.RemoveRange(await db.TotalCategories.Where(c => c.Id == _course || c.Id == _paused).ToListAsync());
                db.Institutions.RemoveRange(await db.Institutions.Where(i => i.Id == _school || i.Id == _otherSchool).ToListAsync());
                db.Users.RemoveRange(await db.Users.Where(u => u.Id == _buyer.Id || u.Id == _other.Id).ToListAsync());
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            await base.CleanServer();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }
    }

    private async Task Seed(int startDays = -1, int endDays = 1)
    {
        using var scope = Server!.Services.CreateScope();
        Assert.IsNull(scope.ServiceProvider.GetService<IWeChatPayService>());
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.TotalCategories.AddRange(new TotalCategory { Id = _course, Title = "Course" }, new TotalCategory { Id = _paused, Title = "Paused course" });
        db.Institutions.AddRange(new Institution { Id = _school, Title = "School" }, new Institution { Id = _otherSchool, Title = "Other school" });
        var course = new VipProduct { Name = "Course membership", Type = VipProductType.Category, TotalCategoryId = _course, PriceInFen = 19800 };
        var paused = new VipProduct { Name = "Paused membership", Type = VipProductType.Category, TotalCategoryId = _paused, IsEnabled = false };
        var school = new VipProduct { Name = "School membership", Type = VipProductType.Institution, InstitutionId = _school };
        var other = new VipProduct { Name = "Other buyer's membership", Type = VipProductType.Institution, InstitutionId = _otherSchool };
        var now = DateTime.UtcNow;
        db.VipMemberships.AddRange(
            new VipMembership { User = _buyer, UserId = _buyer.Id, VipProduct = course, StartTime = now.AddDays(startDays), EndTime = now.AddDays(endDays) },
            new VipMembership { User = _buyer, UserId = _buyer.Id, VipProduct = paused, StartTime = now.AddDays(-1), EndTime = now.AddDays(1) },
            new VipMembership { User = _buyer, UserId = _buyer.Id, VipProduct = school, StartTime = now.AddDays(-1), EndTime = now.AddDays(1) },
            new VipMembership { User = _other, UserId = _other.Id, VipProduct = other, StartTime = now.AddDays(-1), EndTime = now.AddDays(1) });
        db.PaperSnapshots.AddRange(
            new PaperSnapshot { Id = _mockVersion, Title = "Course paper", MockPaper = new MockPaper { Title = "Course template", TotalCategoryId = _course, Status = PaperStatus.Publishable } },
            new PaperSnapshot { Id = _pausedVersion, Title = "Paused course paper", MockPaper = new MockPaper { Title = "Paused template", TotalCategoryId = _paused, Status = PaperStatus.Publishable } },
            new PaperSnapshot { Id = _realVersion, Title = "School paper", RealPaper = new RealPaper { Title = "School template", InstitutionId = _school, Status = PaperStatus.Publishable } },
            new PaperSnapshot { Id = _otherVersion, Title = "Other school paper", RealPaper = new RealPaper { Title = "Other template", InstitutionId = _otherSchool, Status = PaperStatus.Publishable } });
        await db.SaveChangesAsync();
    }

    private void Authenticate() => Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
        WeChatTokenHelper.GenerateJwtToken(_buyer, "isolated-openid", GetService<IConfiguration>()["AppSettings:WeChat:AppSecret"]!));

    [TestMethod]
    public async Task OriginalCatalogAndMembershipUrlsWorkWithoutOrdinaryPayment()
    {
        await Seed();
        using var catalog = await Http.GetAsync($"/api/payment/vip-products?totalCategoryId={_course}");
        Assert.AreEqual(HttpStatusCode.OK, catalog.StatusCode);
        using var json = JsonDocument.Parse(await catalog.Content.ReadAsStringAsync());
        var product = json.RootElement.EnumerateArray().Single();
        Assert.AreEqual("Course membership", product.GetProperty("name").GetString());
        Assert.AreEqual(19800, product.GetProperty("priceInFen").GetInt32());
        Assert.AreEqual(_course, product.GetProperty("totalCategoryId").GetGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/api/payment/vip-products?categoryId={_course}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync("/api/payment/vip-status")).StatusCode);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync("/api/payment/vip-status")).StatusCode);
        Authenticate();
        var status = (await Http.GetFromJsonAsync<VipStatusResponse>("/api/payment/vip-status"))!;
        Assert.AreEqual(3, status.Memberships.Count);
        Assert.IsTrue(status.Memberships.All(m => m.IsActive));
        Assert.AreEqual("Paused course", status.Memberships.Single(m => m.TotalCategoryId == _paused).TotalCategoryName);
        Assert.AreEqual("School", status.Memberships.Single(m => m.InstitutionId == _school).InstitutionName);
        Assert.IsFalse(status.Memberships.Any(m => m.InstitutionId == _otherSchool));
        var pausedCatalog = await Http.GetFromJsonAsync<List<JsonElement>>($"/api/payment/vip-products?totalCategoryId={_paused}");
        Assert.AreEqual(0, pausedCatalog!.Count);
    }

    [TestMethod]
    [DataRow(-1, 1, true)]
    [DataRow(-2, -1, false)]
    [DataRow(1, 2, false)]
    public async Task PaperReadsKeepMembershipBoundariesWithoutOrdinaryPayment(int startDays, int endDays, bool active)
    {
        await Seed(startDays, endDays);
        // Anonymous metadata and access checks must not try to construct the disabled gateway.
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync("/api/MockPapers")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync("/api/RealPapers/institutions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.GetAsync($"/api/MockPapers/snapshots/{_mockVersion}")).StatusCode);
        Authenticate();
        var status = (await Http.GetFromJsonAsync<VipStatusResponse>("/api/payment/vip-status"))!;
        Assert.AreEqual(active, status.Memberships.Single(m => m.TotalCategoryId == _course).IsActive);
        Assert.AreEqual(active ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await Http.GetAsync($"/api/MockPapers/snapshots/{_mockVersion}")).StatusCode);
        // Stopping sales does not revoke a saved membership; school rights remain separate.
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/api/MockPapers/snapshots/{_pausedVersion}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/api/RealPapers/snapshots/{_realVersion}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.GetAsync($"/api/RealPapers/snapshots/{_otherVersion}")).StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(4, await db.VipMemberships.CountAsync(m => m.UserId == _buyer.Id || m.UserId == _other.Id));
        Assert.AreEqual(0, await db.PaymentOrders.CountAsync(o => o.UserId == _buyer.Id || o.UserId == _other.Id));
        Assert.AreEqual(0, await db.PaymentLogs.CountAsync(l => l.PaymentOrder != null &&
            (l.PaymentOrder.UserId == _buyer.Id || l.PaymentOrder.UserId == _other.Id)));
    }
}
