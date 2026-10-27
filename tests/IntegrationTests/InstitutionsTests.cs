using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class InstitutionsTests : TestBase
{
    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        // The existing HTTP fixture shares its in-memory store across hosts.
        db.Institutions.RemoveRange(await db.Institutions.ToListAsync());
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task AdministratorsCanCreateEditAndOrderIndependentInstitutions()
    {
        await LoginAsAdmin();
        int legacyCount;
        using (var scope = Server!.Services.CreateScope())
        {
            legacyCount = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Categories.CountAsync();
        }
        var response = await PostForm("/Institutions/Create", new Dictionary<string, string>
        {
            ["Title"] = "  Wuhan University  ",
            ["OrderIndex"] = "10"
        });
        AssertRedirect(response, "/Institutions");

        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var institution = await db.Institutions.SingleAsync();
            Assert.AreEqual("Wuhan University", institution.Title);
            Assert.AreEqual(10, institution.OrderIndex);
            Assert.AreEqual(legacyCount, await db.Categories.CountAsync());
            id = institution.Id;
        }

        response = await PostForm($"/Institutions/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Title"] = "Wuhan Conservatory",
            ["OrderIndex"] = "1"
        });
        AssertRedirect(response, "/Institutions");
        await PostForm("/Institutions/Create", new Dictionary<string, string>
        {
            ["Title"] = "Suzhou University",
            ["OrderIndex"] = "2"
        });
        var index = await Http.GetStringAsync("/Institutions");
        Assert.Contains($"/Institutions/Edit/{id}", index);
        Assert.IsTrue(index.IndexOf("Wuhan Conservatory", StringComparison.Ordinal) <
                      index.IndexOf("Suzhou University", StringComparison.Ordinal));
        using var verifyScope = Server!.Services.CreateScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
            .Institutions.SingleAsync(c => c.Id == id);
        Assert.AreEqual("Wuhan Conservatory", stored.Title);
        Assert.AreEqual(1, stored.OrderIndex);
    }

    [TestMethod]
    public async Task InvalidInputDoesNotCreateOrOverwriteAnInstitution()
    {
        await LoginAsAdmin();
        foreach (var title in new[] { "   ", new string('x', 201) })
        {
            var invalid = await PostForm("/Institutions/Create", new Dictionary<string, string>
            {
                ["Title"] = title,
                ["OrderIndex"] = "0"
            });
            Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        }
        var invalidOrder = await PostForm("/Institutions/Create", new Dictionary<string, string>
        {
            ["Title"] = "Wuhan University",
            ["OrderIndex"] = "-1"
        });
        Assert.AreEqual(HttpStatusCode.OK, invalidOrder.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
                .Institutions.CountAsync());
        }

        await PostForm("/Institutions/Create", new Dictionary<string, string> { ["Title"] = "Wuhan University" });
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
                .Institutions.Select(c => c.Id).SingleAsync();
        }
        var edit = await PostForm($"/Institutions/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Title"] = "",
            ["OrderIndex"] = "5"
        });
        Assert.AreEqual(HttpStatusCode.OK, edit.StatusCode);
        var editHtml = await edit.Content.ReadAsStringAsync();
        Assert.Contains(id.ToString(), editHtml);
        using var verifyScope = Server!.Services.CreateScope();
        var saved = await verifyScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
            .Institutions.SingleAsync();
        Assert.AreEqual("Wuhan University", saved.Title);
        Assert.AreEqual(0, saved.OrderIndex);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/Institutions/Edit/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task ManagementRequiresPaperPermissionAndValidAntiforgeryToken()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/Institutions")).StatusCode);
        await RegisterAndLoginAsync();
        foreach (var url in new[] { "/Institutions", "/Institutions/Create", $"/Institutions/Edit/{Guid.NewGuid()}" })
        {
            var response = await Http.GetAsync(url);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        }
        var deniedPost = await PostForm("/Institutions/Create",
            new Dictionary<string, string> { ["Title"] = "Not permitted" }, includeToken: false);
        Assert.IsTrue(deniedPost.StatusCode != HttpStatusCode.OK);
        await LoginAsAdmin();
        var noToken = await PostForm("/Institutions/Create",
            new Dictionary<string, string> { ["Title"] = "No token" }, includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Institutions.CountAsync());
    }

    [TestMethod]
    public async Task SearchAndPagingKeepEveryInstitutionReachable()
    {
        await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Institutions.AddRange(Enumerable.Range(0, 25).Select(i => new Institution { Title = $"College {i:00}", OrderIndex = i }));
            db.Institutions.Add(new Institution { Title = "University", OrderIndex = 100 });
            await db.SaveChangesAsync();
        }
        var first = await Http.GetStringAsync("/Institutions?search=College");
        Assert.Contains("College 00", first);
        Assert.DoesNotContain("College 24", first);
        Assert.Contains("search=College", first);
        var second = await Http.GetStringAsync("/Institutions?search=College&page=2");
        Assert.Contains("College 24", second);
        Assert.DoesNotContain("College 00", second);
        Assert.DoesNotContain(">University<", second);
        Assert.Contains("College 24", await Http.GetStringAsync("/Institutions?search=College&page=999"));
        Assert.Contains("No matching institutions", await Http.GetStringAsync("/Institutions?search=missing"));
    }

    [TestMethod]
    public async Task AddingInstitutionsPreservesExistingPaperAndTagRelationships()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddInstitutions", StringComparison.Ordinal));
        await migrator.MigrateAsync(migrations[index - 1]);
        var tag = new Tag { DisplayName = "School navigation", NormalizedName = "SCHOOL NAVIGATION" };
        var paper = new Paper { Title = "Existing real exam", IsRealExam = true, TimeLimit = 180 };
        db.AddRange(tag, paper);
        await db.SaveChangesAsync();
        db.PaperTags.Add(new PaperTag { PaperId = paper.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.AreEqual(0, await db.Institutions.CountAsync());
        var saved = await db.Papers.Include(p => p.PaperTags).SingleAsync();
        Assert.AreEqual(paper.Id, saved.Id);
        Assert.AreEqual(180, saved.TimeLimit);
        Assert.IsTrue(saved.IsRealExam);
        Assert.AreEqual(tag.Id, saved.PaperTags.Single().TagId);
        var institution = new Institution { Title = "Wuhan University" };
        db.Institutions.Add(institution);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.AreEqual(institution.Id, (await db.Institutions.SingleAsync()).Id);
        // Creating the independent entity does not reinterpret a tag or enroll a paper.
        Assert.AreEqual(1, await db.Tags.CountAsync());
        Assert.AreEqual(1, await db.PaperTags.CountAsync());
    }
}
