using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class MockPapersTests : TestBase
{
    private Guid _course;
    private Guid _otherCourse;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.MockPapers.RemoveRange(await db.MockPapers.ToListAsync());
        var course = new TotalCategory { Title = "Mock paper course" };
        var otherCourse = new TotalCategory { Title = "Other mock paper course" };
        db.TotalCategories.AddRange(course, otherCourse);
        await db.SaveChangesAsync();
        _course = course.Id;
        _otherCourse = otherCourse.Id;
    }

    private Dictionary<string, string> Form(string title = "Course practice") => new()
    {
        ["Title"] = title, ["TotalCategoryId"] = _course.ToString(), ["TimeLimit"] = "180"
    };

    [TestMethod]
    public async Task EditorCreatesOnlyDraftsAndCanChangeCourseDurationAndOpening()
    {
        await LoginAsAdmin();
        int legacyPapers;
        using (var scope = Server!.Services.CreateScope())
            legacyPapers = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Papers.CountAsync();
        var form = Form("  Course practice  ");
        var forgedId = Guid.NewGuid();
        form["Id"] = forgedId.ToString();
        form["Status"] = "2";
        AssertRedirect(await PostForm("/MockPapers/Create", form), "/MockPapers");
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await db.MockPapers.SingleAsync();
            Assert.AreNotEqual(forgedId, paper.Id);
            Assert.AreEqual("Course practice", paper.Title);
            Assert.AreEqual(_course, paper.TotalCategoryId);
            Assert.AreEqual(180, paper.TimeLimit);
            Assert.AreEqual(PaperStatus.Draft, paper.Status);
            Assert.IsFalse(paper.AllowViewAnonymousely);
            Assert.AreEqual(legacyPapers, await db.Papers.CountAsync());
            id = paper.Id;
        }
        form["Id"] = id.ToString();
        form["Title"] = "Updated practice";
        form["TotalCategoryId"] = _otherCourse.ToString();
        form["TimeLimit"] = "120";
        form["AllowViewAnonymousely"] = "true";
        AssertRedirect(await PostForm($"/MockPapers/Edit/{id}", form), "/MockPapers");
        var html = await Http.GetStringAsync($"/MockPapers/Edit/{id}");
        Assert.Contains("Updated practice", html);
        Assert.Contains("Save Draft", html);
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.SingleAsync();
        Assert.AreEqual(_otherCourse, saved.TotalCategoryId);
        Assert.AreEqual(120, saved.TimeLimit);
        Assert.IsTrue(saved.AllowViewAnonymousely);
        Assert.AreEqual(PaperStatus.Draft, saved.Status);
    }

    [TestMethod]
    public async Task InvalidInputKeepsCourseOptionsAndDoesNotOverwriteDraft()
    {
        await LoginAsAdmin();
        foreach (var (field, value) in new[]
                 {
                     ("Title", "  "), ("Title", new string('x', 201)), ("TimeLimit", "0"), ("TimeLimit", "-1"),
                     ("TotalCategoryId", ""), ("TotalCategoryId", Guid.Empty.ToString()),
                     ("TotalCategoryId", Guid.NewGuid().ToString()), ("TotalCategoryId", "invalid")
                 })
        {
            var form = Form();
            form[field] = value;
            var response = await PostForm("/MockPapers/Create", form);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(_otherCourse.ToString(), await response.Content.ReadAsStringAsync());
        }
        using (var scope = Server!.Services.CreateScope())
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.CountAsync());
        await PostForm("/MockPapers/Create", Form());
        Guid id;
        using (var scope = Server!.Services.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.Select(p => p.Id).SingleAsync();
        var edit = Form("Should not save");
        edit["Id"] = id.ToString();
        edit["TotalCategoryId"] = Guid.NewGuid().ToString();
        var invalid = await PostForm($"/MockPapers/Edit/{id}", edit);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Select an existing course.", html);
        Assert.Contains(id.ToString(), html);
        edit["Id"] = Guid.NewGuid().ToString();
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/MockPapers/Edit/{id}", edit)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/MockPapers/Edit/{Guid.NewGuid()}")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.SingleAsync();
        Assert.AreEqual("Course practice", saved.Title);
        Assert.AreEqual(_course, saved.TotalCategoryId);
    }

    [TestMethod]
    public async Task FrozenTemplatesCannotBeEditedOrReopenedThroughPostedStatus()
    {
        await LoginAsAdmin();
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = new MockPaper { Title = "Frozen practice", TotalCategoryId = _course, Status = PaperStatus.Frozen };
            db.MockPapers.Add(paper);
            await db.SaveChangesAsync();
            id = paper.Id;
        }
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.GetAsync($"/MockPapers/Edit/{id}")).StatusCode);
        var form = Form();
        form["Id"] = id.ToString();
        form["Status"] = "0";
        Assert.AreEqual(HttpStatusCode.Conflict, (await PostForm($"/MockPapers/Edit/{id}", form)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.SingleAsync();
        Assert.AreEqual("Frozen practice", stored.Title);
        Assert.AreEqual(PaperStatus.Frozen, stored.Status);
    }

    [TestMethod]
    public async Task ManagementRequiresPaperPermissionAndAntiforgery()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/MockPapers")).StatusCode);
        await RegisterAndLoginAsync();
        foreach (var url in new[] { "/MockPapers", "/MockPapers/Create", $"/MockPapers/Edit/{Guid.NewGuid()}" })
        {
            var response = await Http.GetAsync(url);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        }
        var denied = await PostForm("/MockPapers/Create", Form(), includeToken: false);
        Assert.IsTrue(denied.StatusCode != HttpStatusCode.OK);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await PostForm("/MockPapers/Create", Form(), includeToken: false)).StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.CountAsync());
    }

    [TestMethod]
    public async Task EditingPermissionDoesNotGrantCreationPermission()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanEditPapers))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string>
        {
            ["EmailOrUserName"] = email, ["Password"] = password
        });
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paper = new MockPaper { Title = "Existing draft", TotalCategoryId = _course };
        db.MockPapers.Add(paper);
        await db.SaveChangesAsync();
        var index = await Http.GetStringAsync("/MockPapers");
        Assert.Contains($"/MockPapers/Edit/{paper.Id}", index);
        Assert.DoesNotContain("href=\"/MockPapers/Create\"", index);
        var archive = await Http.GetStringAsync("/Papers");
        Assert.DoesNotContain("href=\"/MockPapers/Create\"", archive);
        Assert.DoesNotContain("href=\"/RealPapers/Create\"", archive);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/MockPapers/Edit/{paper.Id}")).StatusCode);
        var form = Form();
        form["__RequestVerificationToken"] = await GetAntiCsrfToken($"/MockPapers/Edit/{paper.Id}");
        foreach (var response in new[]
                 {
                     await Http.GetAsync("/MockPapers/Create"),
                     await PostForm("/MockPapers/Create", form)
                 })
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        Assert.AreEqual(1, await db.MockPapers.CountAsync());
    }

    [TestMethod]
    public async Task CourseFilterAndSearchSurvivePagination()
    {
        await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.MockPapers.AddRange(Enumerable.Range(0, 25).Select(i => new MockPaper
            {
                Title = $"Practice {i:00}", TotalCategoryId = _course, CreationTime = DateTime.UtcNow.AddDays(-i)
            }));
            db.MockPapers.Add(new MockPaper { Title = "Practice excluded", TotalCategoryId = _otherCourse });
            await db.SaveChangesAsync();
        }
        var url = $"/MockPapers?search=Practice&totalCategoryId={_course}";
        var first = await Http.GetStringAsync(url);
        Assert.Contains("Practice 00", first);
        Assert.DoesNotContain("Practice 24", first);
        Assert.DoesNotContain("Practice excluded", first);
        Assert.Contains($"totalCategoryId={_course}", first);
        Assert.Contains("search=Practice", first);
        var second = await Http.GetStringAsync(url + "&page=2");
        Assert.Contains("Practice 24", second);
        Assert.DoesNotContain("Practice 00", second);
        Assert.Contains("Practice 24", await Http.GetStringAsync(url + "&page=999"));
        Assert.Contains("No matching papers", await Http.GetStringAsync("/MockPapers?search=missing"));
    }

    [TestMethod]
    public async Task MigrationPreservesExistingPapersAndRequiresAnExistingCourse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddMockPapers", StringComparison.Ordinal));
        await migrator.MigrateAsync(migrations[index - 1]);
        var course = new TotalCategory { Title = "Course" };
        var legacy = new Paper { Title = "Existing paper", IsRealExam = true, TimeLimit = 180 };
        db.AddRange(course, legacy);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.AreEqual(0, await db.MockPapers.CountAsync());
        db.ChangeTracker.Clear();
        var retained = await db.Papers.SingleAsync();
        Assert.AreEqual(legacy.Id, retained.Id);
        Assert.AreEqual("Existing paper", retained.Title);
        Assert.IsTrue(retained.IsRealExam);
        Assert.AreEqual(180, retained.TimeLimit);
        db.MockPapers.Add(new MockPaper { Title = "New draft", TotalCategoryId = course.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.TotalCategories.Remove(await db.TotalCategories.SingleAsync());
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.MockPapers.Add(new MockPaper { Title = "Missing course", TotalCategoryId = Guid.NewGuid() });
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, await db.MockPapers.CountAsync());
        Assert.AreEqual(PaperStatus.Draft, (await db.MockPapers.SingleAsync()).Status);
    }
}
