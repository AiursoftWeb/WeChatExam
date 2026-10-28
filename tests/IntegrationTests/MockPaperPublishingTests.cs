using System.Net;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class MockPaperPublishingTests : TestBase
{
    private async Task<(Guid Paper, Guid Question, Guid Course)> Seed(bool withQuestion = true)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var paper = new MockPaper { Title = "Publication draft", TotalCategory = period.TotalCategory!, TotalCategoryId = period.TotalCategoryId };
        var question = new Question
        {
            Content = "Original question", QuestionCategory = period, QuestionType = QuestionType.Choice,
            StandardAnswer = "Original answer", Metadata = "Original options", Explanation = "Original explanation"
        };
        db.AddRange(paper, question);
        if (withQuestion) db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaper = paper, Question = question, Order = 3, Score = 8 });
        await db.SaveChangesAsync();
        return (paper.Id, question.Id, period.TotalCategoryId);
    }

    private Task<HttpResponseMessage> Publish(Guid id) => PostForm($"/MockPapers/Publish/{id}", [], tokenUrl: "/MockPapers/Create");

    [TestMethod]
    public async Task PublishingCopiesFullPayloadAndLaterEditsCreateANewVersion()
    {
        await LoginAsAdmin();
        var (id, questionId, _) = await Seed();
        var first = await Publish(id);
        Assert.AreEqual(HttpStatusCode.Found, first.StatusCode);
        Guid firstId;
        int oldPaperCount;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var snapshot = await db.PaperSnapshots.Include(s => s.QuestionSnapshots).SingleAsync(s => s.MockPaperId == id);
            firstId = snapshot.Id;
            Assert.IsNull(snapshot.PaperId);
            Assert.AreEqual(1, snapshot.Version);
            Assert.AreEqual(180, snapshot.TimeLimit);
            Assert.IsFalse(snapshot.IsFree);
            var copied = snapshot.QuestionSnapshots.Single();
            Assert.AreEqual(questionId, copied.OriginalQuestionId);
            Assert.AreEqual("Original options", copied.Metadata);
            Assert.AreEqual("Original answer", copied.StandardAnswer);
            Assert.AreEqual("Original explanation", copied.Explanation);
            Assert.AreEqual(3, copied.Order);
            Assert.AreEqual(8, copied.Score);
            oldPaperCount = await db.Papers.CountAsync();
            (await db.Questions.SingleAsync(q => q.Id == questionId)).Content = "Updated question";
            await db.SaveChangesAsync();
        }
        var edit = await PostForm($"/MockPapers/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Title"] = "Updated draft", ["TimeLimit"] = "120",
            ["TotalCategoryId"] = (await ReadCourse(id)).ToString(), ["AllowViewAnonymousely"] = "true"
        });
        Assert.AreEqual(HttpStatusCode.Found, edit.StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await Publish(id)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var versions = await context.PaperSnapshots.Include(s => s.QuestionSnapshots).Where(s => s.MockPaperId == id).OrderBy(s => s.Version).ToListAsync();
        Assert.AreEqual(2, versions.Count);
        Assert.AreEqual(firstId, versions[0].Id);
        Assert.AreEqual("Publication draft", versions[0].Title);
        Assert.AreEqual(180, versions[0].TimeLimit);
        Assert.IsFalse(versions[0].IsFree);
        Assert.AreEqual("Original question", versions[0].QuestionSnapshots.Single().Content);
        Assert.AreEqual("Updated draft", versions[1].Title);
        Assert.AreEqual(120, versions[1].TimeLimit);
        Assert.IsTrue(versions[1].IsFree);
        Assert.AreEqual("Updated question", versions[1].QuestionSnapshots.Single().Content);
        Assert.AreEqual(2, versions[1].Version);
        Assert.AreEqual(PaperStatus.Publishable, (await context.MockPapers.SingleAsync(p => p.Id == id)).Status);
        Assert.AreEqual(oldPaperCount, await context.Papers.CountAsync());
        var html = await Http.GetStringAsync($"/MockPapers/Version/{id}?snapshotId={firstId}");
        Assert.Contains("Original question", html);
        Assert.DoesNotContain("Updated question", html);
        Assert.Contains("Original answer", html);
    }

    private async Task<Guid> ReadCourse(Guid id)
    {
        using var scope = Server!.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPapers.SingleAsync(p => p.Id == id)).TotalCategoryId;
    }

    [TestMethod]
    public async Task InvalidDraftCannotPublishAnEmptyOrCrossCourseVersion()
    {
        await LoginAsAdmin();
        var (id, question, _) = await Seed(false);
        var empty = await Publish(id);
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Add at least one question", await empty.Content.ReadAsStringAsync());
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var other = QuestionPeriodAssignmentTests.TestPeriod();
            db.QuestionCategories.Add(other);
            (await db.Questions.SingleAsync(q => q.Id == question)).QuestionCategory = other;
            db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaperId = id, QuestionId = question, Score = 10 });
            await db.SaveChangesAsync();
        }
        var invalid = await Publish(id);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Every question must belong", await invalid.Content.ReadAsStringAsync());
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await context.PaperSnapshots.AnyAsync(s => s.MockPaperId == id));
        Assert.AreEqual(PaperStatus.Draft, (await context.MockPapers.SingleAsync(p => p.Id == id)).Status);
    }

    [TestMethod]
    [DataRow(-1, 10)]
    [DataRow(1, 0)]
    public async Task InvalidOrderOrScoreCannotLeaveAPartialVersion(int order, int score)
    {
        await LoginAsAdmin();
        var (id, _, _) = await Seed();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var link = await db.MockPaperQuestions.SingleAsync(l => l.MockPaperId == id);
            link.Order = order; link.Score = score;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.OK, (await Publish(id)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await context.PaperSnapshots.AnyAsync(s => s.MockPaperId == id));
        Assert.AreEqual(PaperStatus.Draft, (await context.MockPapers.SingleAsync(p => p.Id == id)).Status);
    }

    [TestMethod]
    public async Task VersionsAndSavedQuestionsArePagedWithinTheirSource()
    {
        await LoginAsAdmin();
        var (id, _, _) = await Seed();
        Guid snapshotId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.PaperSnapshots.AddRange(Enumerable.Range(1, 25).Select(i => new PaperSnapshot
            { Id = Guid.NewGuid(), MockPaperId = id, Title = $"Saved draft {i:00}", Version = i }));
            var latest = db.ChangeTracker.Entries<PaperSnapshot>().Select(e => e.Entity).Single(s => s.Version == 25);
            snapshotId = latest.Id;
            db.QuestionSnapshots.AddRange(Enumerable.Range(1, 21).Select(i => new QuestionSnapshot
            { Id = Guid.NewGuid(), PaperSnapshotId = snapshotId, Order = i, Score = 10, Content = $"Saved question {i:00}" }));
            await db.SaveChangesAsync();
        }
        var first = await Http.GetStringAsync($"/MockPapers/Versions/{id}");
        Assert.Contains("Saved draft 25", first);
        Assert.DoesNotContain("Saved draft 01", first);
        var second = await Http.GetStringAsync($"/MockPapers/Versions/{id}?page=2");
        Assert.Contains("Saved draft 01", second);
        Assert.DoesNotContain("Saved draft 25", second);
        var page = await Http.GetStringAsync($"/MockPapers/Version/{id}?snapshotId={snapshotId}");
        Assert.Contains("Saved question 01", page);
        Assert.DoesNotContain("Saved question 21", page);
        var last = await Http.GetStringAsync($"/MockPapers/Version/{id}?snapshotId={snapshotId}&page=999");
        Assert.Contains("Saved question 21", last);
        Assert.DoesNotContain("Saved question 01", last);
    }

    [TestMethod]
    public async Task PublishedOwnershipRemainsFixedEvenAfterRemovingAllLiveQuestions()
    {
        await LoginAsAdmin();
        var (id, question, course) = await Seed();
        await Publish(id);
        Guid otherCourse;
        Guid snapshotId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var other = new TotalCategory { Title = "Another course" };
            db.TotalCategories.Add(other);
            await db.SaveChangesAsync();
            otherCourse = other.Id;
            snapshotId = (await db.PaperSnapshots.SingleAsync(s => s.MockPaperId == id)).Id;
        }
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/MockPapers/RemoveQuestion/{id}", new Dictionary<string, string> { ["QuestionId"] = question.ToString() }, tokenUrl: $"/MockPapers/Questions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/Questions/Delete/{question}", new Dictionary<string, string> { ["id"] = question.ToString() })).StatusCode);
        var rejected = await PostForm($"/MockPapers/Edit/{id}", new Dictionary<string, string>
        { ["Id"] = id.ToString(), ["Title"] = "Moved", ["TotalCategoryId"] = otherCourse.ToString(), ["TimeLimit"] = "120" });
        Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("A published paper cannot change courses", await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(course, await ReadCourse(id));
        Assert.Contains("Original question", await Http.GetStringAsync($"/MockPapers/Version/{id}?snapshotId={snapshotId}"));
    }

    [TestMethod]
    public async Task PublishingAndAnswersRequireBothPaperAndQuestionPermissions()
    {
        var (id, _, _) = await Seed();
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/MockPapers/Versions/{id}")).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var user = await manager.FindByEmailAsync(email);
            await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(AppPermissions.Type, AppPermissionNames.CanEditPapers));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var denied = await Http.GetAsync($"/MockPapers/Versions/{id}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        denied = await PostForm($"/MockPapers/Publish/{id}", [], tokenUrl: $"/MockPapers/Edit/{id}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm($"/MockPapers/Publish/{id}", [], includeToken: false)).StatusCode);
        await Publish(id);
        using var verify = Server!.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var snapshot = await db.PaperSnapshots.SingleAsync(s => s.MockPaperId == id);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/MockPapers/Version/{Guid.NewGuid()}?snapshotId={snapshot.Id}")).StatusCode);
        (await db.MockPapers.SingleAsync(p => p.Id == id)).Status = PaperStatus.Frozen;
        await db.SaveChangesAsync();
        Assert.AreEqual(HttpStatusCode.Conflict, (await Publish(id)).StatusCode);
        Assert.AreEqual(1, await db.PaperSnapshots.CountAsync(s => s.MockPaperId == id));
    }

    [TestMethod]
    public async Task MigrationPreservesHistoricalSnapshotsAndRestrictsNewSources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028031253_AddMockPaperQuestions");
        var legacy = new Paper { Title = "Legacy paper" };
        var course = new TotalCategory { Title = "Mock course" };
        var mock = new MockPaper { Title = "Mock paper", TotalCategory = course, TotalCategoryId = course.Id };
        db.AddRange(legacy, mock);
        await db.SaveChangesAsync();
        var old = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PaperSnapshots (Id,PaperId,Version,Title,TimeLimit,IsFree,CreationTime) VALUES ({old},{legacy.Id},1,'Historical payload',180,0,CURRENT_TIMESTAMP)");
        await migrator.MigrateAsync();
        var retained = await db.PaperSnapshots.SingleAsync();
        Assert.AreEqual(old, retained.Id);
        Assert.AreEqual(legacy.Id, retained.PaperId);
        Assert.IsNull(retained.MockPaperId);
        Assert.AreEqual("Historical payload", retained.Title);
        db.PaperSnapshots.Add(new PaperSnapshot { Id = Guid.NewGuid(), MockPaperId = mock.Id, Version = 1, Title = "New version" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.PaperSnapshots.Add(new PaperSnapshot { Id = Guid.NewGuid(), MockPaperId = mock.Id, Version = 1, Title = "Duplicate version" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.PaperSnapshots.Add(new PaperSnapshot { Id = Guid.NewGuid(), MockPaperId = mock.Id, PaperId = legacy.Id, Version = 2, Title = "Two sources" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.MockPapers.Where(p => p.Id == mock.Id).ExecuteDeleteAsync());
        Assert.AreEqual(2, await db.PaperSnapshots.CountAsync());
    }
}
