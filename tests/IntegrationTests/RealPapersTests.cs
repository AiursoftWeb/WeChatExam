using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class RealPapersTests : TestBase
{
    private Guid _institution;
    private Guid _otherInstitution;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.RealPapers.RemoveRange(await db.RealPapers.ToListAsync());
        var institution = new Institution { Title = "Real paper institution" };
        var otherInstitution = new Institution { Title = "Other real paper institution" };
        db.Institutions.AddRange(institution, otherInstitution);
        await db.SaveChangesAsync();
        _institution = institution.Id;
        _otherInstitution = otherInstitution.Id;
    }

    private Dictionary<string, string> Form(string title = "Institution practice") => new()
    {
        ["Title"] = title, ["InstitutionId"] = _institution.ToString(), ["TimeLimit"] = "180"
    };

    [TestMethod]
    public async Task EditorCreatesOnlyDraftsAndCanChangeInstitutionAndDuration()
    {
        await LoginAsAdmin();
        int legacyPapers;
        using (var scope = Server!.Services.CreateScope())
            legacyPapers = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Papers.CountAsync();
        var form = Form("  Institution practice  ");
        var forgedId = Guid.NewGuid();
        form["Id"] = forgedId.ToString();
        form["Status"] = "2";
        form["IsFree"] = "true";
        form["TotalCategoryId"] = Guid.NewGuid().ToString();
        AssertRedirect(await PostForm("/RealPapers/Create", form), "/RealPapers");
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await db.RealPapers.SingleAsync();
            Assert.AreNotEqual(forgedId, paper.Id);
            Assert.AreEqual("Institution practice", paper.Title);
            Assert.AreEqual(_institution, paper.InstitutionId);
            Assert.AreEqual(180, paper.TimeLimit);
            Assert.AreEqual(PaperStatus.Draft, paper.Status);
            Assert.AreEqual(legacyPapers, await db.Papers.CountAsync());
            id = paper.Id;
        }
        form["Id"] = id.ToString();
        form["Title"] = "Updated practice";
        form["InstitutionId"] = _otherInstitution.ToString();
        form["TimeLimit"] = "120";
        form["AllowViewAnonymousely"] = "true"; // Legacy opening fields must not change real-paper semantics.
        AssertRedirect(await PostForm($"/RealPapers/Edit/{id}", form), "/RealPapers");
        var html = await Http.GetStringAsync($"/RealPapers/Edit/{id}");
        Assert.Contains("Updated practice", html);
        Assert.Contains("Save Draft", html);
        Assert.DoesNotContain("name=\"AllowViewAnonymousely\"", html);
        Assert.DoesNotContain("name=\"TotalCategoryId\"", html);
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.SingleAsync();
        Assert.AreEqual(_otherInstitution, saved.InstitutionId);
        Assert.AreEqual(120, saved.TimeLimit);
        Assert.AreEqual(PaperStatus.Draft, saved.Status);
    }

    [TestMethod]
    public async Task InvalidInputKeepsInstitutionOptionsAndDoesNotOverwriteDraft()
    {
        await LoginAsAdmin();
        foreach (var (field, value) in new[]
                 {
                     ("Title", "  "), ("Title", new string('x', 201)), ("TimeLimit", "0"), ("TimeLimit", "-1"),
                     ("InstitutionId", ""), ("InstitutionId", Guid.Empty.ToString()),
                     ("InstitutionId", Guid.NewGuid().ToString()), ("InstitutionId", "invalid")
                 })
        {
            var form = Form();
            form[field] = value;
            var response = await PostForm("/RealPapers/Create", form);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(_otherInstitution.ToString(), await response.Content.ReadAsStringAsync());
        }
        using (var scope = Server!.Services.CreateScope())
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.CountAsync());
        await PostForm("/RealPapers/Create", Form());
        Guid id;
        using (var scope = Server!.Services.CreateScope())
            id = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.Select(p => p.Id).SingleAsync();
        var edit = Form("Should not save");
        edit["Id"] = id.ToString();
        edit["InstitutionId"] = Guid.NewGuid().ToString();
        var invalid = await PostForm($"/RealPapers/Edit/{id}", edit);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Select an existing institution.", html);
        Assert.Contains(id.ToString(), html);
        edit["Id"] = Guid.NewGuid().ToString();
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/RealPapers/Edit/{id}", edit)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/RealPapers/Edit/{Guid.NewGuid()}")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.SingleAsync();
        Assert.AreEqual("Institution practice", saved.Title);
        Assert.AreEqual(_institution, saved.InstitutionId);
    }

    [TestMethod]
    public async Task FrozenTemplatesCannotBeEditedOrReopenedThroughPostedStatus()
    {
        await LoginAsAdmin();
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = new RealPaper { Title = "Frozen practice", InstitutionId = _institution, Status = PaperStatus.Frozen };
            db.RealPapers.Add(paper);
            await db.SaveChangesAsync();
            id = paper.Id;
        }
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.GetAsync($"/RealPapers/Edit/{id}")).StatusCode);
        var form = Form();
        form["Id"] = id.ToString();
        form["Status"] = "0";
        Assert.AreEqual(HttpStatusCode.Conflict, (await PostForm($"/RealPapers/Edit/{id}", form)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.SingleAsync();
        Assert.AreEqual("Frozen practice", stored.Title);
        Assert.AreEqual(PaperStatus.Frozen, stored.Status);
    }

    [TestMethod]
    public async Task ManagementRequiresPaperPermissionAndAntiforgery()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/RealPapers")).StatusCode);
        await RegisterAndLoginAsync();
        foreach (var url in new[] { "/RealPapers", "/RealPapers/Create", $"/RealPapers/Edit/{Guid.NewGuid()}" })
        {
            var response = await Http.GetAsync(url);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        }
        var denied = await PostForm("/RealPapers/Create", Form(), includeToken: false);
        Assert.IsTrue(denied.StatusCode != HttpStatusCode.OK);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await PostForm("/RealPapers/Create", Form(), includeToken: false)).StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.CountAsync());
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
        var paper = new RealPaper { Title = "Existing draft", InstitutionId = _institution };
        db.RealPapers.Add(paper);
        await db.SaveChangesAsync();
        var index = await Http.GetStringAsync("/RealPapers");
        Assert.Contains($"/RealPapers/Edit/{paper.Id}", index);
        Assert.DoesNotContain("href=\"/RealPapers/Create\"", index);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/RealPapers/Edit/{paper.Id}")).StatusCode);
        var form = Form();
        form["__RequestVerificationToken"] = await GetAntiCsrfToken($"/RealPapers/Edit/{paper.Id}");
        foreach (var response in new[]
                 {
                     await Http.GetAsync("/RealPapers/Create"),
                     await PostForm("/RealPapers/Create", form)
                 })
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        Assert.AreEqual(1, await db.RealPapers.CountAsync());
    }

    [TestMethod]
    public async Task InstitutionFilterAndSearchSurvivePagination()
    {
        await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.RealPapers.AddRange(Enumerable.Range(0, 25).Select(i => new RealPaper
            {
                Title = $"Practice {i:00}", InstitutionId = _institution, CreationTime = DateTime.UtcNow.AddDays(-i)
            }));
            db.RealPapers.Add(new RealPaper { Title = "Practice excluded", InstitutionId = _otherInstitution });
            await db.SaveChangesAsync();
        }
        var url = $"/RealPapers?search=Practice&institutionId={_institution}";
        var first = await Http.GetStringAsync(url);
        Assert.Contains("Practice 00", first);
        Assert.DoesNotContain("Practice 24", first);
        Assert.DoesNotContain("Practice excluded", first);
        Assert.Contains($"institutionId={_institution}", first);
        Assert.Contains("search=Practice", first);
        var second = await Http.GetStringAsync(url + "&page=2");
        Assert.Contains("Practice 24", second);
        Assert.DoesNotContain("Practice 00", second);
        Assert.Contains("Practice 24", await Http.GetStringAsync(url + "&page=999"));
        Assert.Contains("No matching papers", await Http.GetStringAsync("/RealPapers?search=missing"));
    }

    [TestMethod]
    public async Task InstitutionCardOpensItsPapersAndPreselectsTheNewDraftOwner()
    {
        await LoginAsAdmin();
        var institutions = await Http.GetStringAsync("/Institutions");
        Assert.Contains($"/RealPapers?institutionId={_institution}", institutions);
        Assert.Contains($"/Institutions/Edit/{_institution}", institutions);
        var list = await Http.GetStringAsync($"/RealPapers?institutionId={_institution}");
        Assert.Contains($"/RealPapers/Create?institutionId={_institution}", list);
        var editor = await Http.GetStringAsync($"/RealPapers/Create?institutionId={_institution}");
        var option = System.Text.RegularExpressions.Regex.Match(editor,
            $"<option(?=[^>]*value=\"{_institution}\")(?=[^>]*selected=\"selected\")[^>]*>");
        Assert.IsTrue(option.Success, "The institution selected from the list must remain selected in the draft editor.");
        Assert.DoesNotContain("name=\"IsFree\"", editor);
        Assert.DoesNotContain("name=\"TotalCategoryId\"", editor);
    }

    [TestMethod]
    public async Task MigrationPreservesExistingPapersAndRequiresAnExistingInstitution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddRealPapers", StringComparison.Ordinal));
        await migrator.MigrateAsync(migrations[index - 1]);
        var institution = new Institution { Title = "Institution" };
        var legacy = new Paper { Title = "Existing paper", IsRealExam = true, TimeLimit = 180 };
        db.AddRange(institution, legacy);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.AreEqual(0, await db.RealPapers.CountAsync());
        db.ChangeTracker.Clear();
        var retained = await db.Papers.SingleAsync();
        Assert.AreEqual(legacy.Id, retained.Id);
        Assert.AreEqual("Existing paper", retained.Title);
        Assert.IsTrue(retained.IsRealExam);
        Assert.AreEqual(180, retained.TimeLimit);
        db.RealPapers.Add(new RealPaper { Title = "New draft", InstitutionId = institution.Id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.Institutions.Remove(await db.Institutions.SingleAsync());
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.RealPapers.Add(new RealPaper { Title = "Missing institution", InstitutionId = Guid.NewGuid() });
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, await db.RealPapers.CountAsync());
        Assert.AreEqual(PaperStatus.Draft, (await db.RealPapers.SingleAsync()).Status);
    }
}
