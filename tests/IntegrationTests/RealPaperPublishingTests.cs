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
public class RealPaperPublishingTests : TestBase
{
    private async Task<(Guid Paper, Guid Question, Guid Institution)> Seed(bool withQuestion = true)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var owner = new Institution { Title = "Publication institution" };
        var paper = new RealPaper { Title = "Publication draft", Institution = owner, InstitutionId = owner.Id };
        var question = new Question
        {
            Content = "Original question", QuestionCategory = period, QuestionType = QuestionType.Choice,
            StandardAnswer = "Original answer", Metadata = "Original options", Explanation = "Original explanation"
        };
        db.AddRange(paper, question);
        if (withQuestion) db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaper = paper, Question = question, Order = 3, Score = 8 });
        await db.SaveChangesAsync();
        return (paper.Id, question.Id, owner.Id);
    }

    private Task<HttpResponseMessage> Publish(Guid id) => PostForm($"/RealPapers/Publish/{id}", [], tokenUrl: "/RealPapers/Create");

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
            var snapshot = await db.PaperSnapshots.Include(s => s.QuestionSnapshots).SingleAsync(s => s.RealPaperId == id);
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
        var edit = await PostForm($"/RealPapers/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Title"] = "Updated draft", ["TimeLimit"] = "120",
            ["InstitutionId"] = (await ReadInstitution(id)).ToString()
        });
        Assert.AreEqual(HttpStatusCode.Found, edit.StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await Publish(id)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var versions = await context.PaperSnapshots.Include(s => s.QuestionSnapshots).Where(s => s.RealPaperId == id).OrderBy(s => s.Version).ToListAsync();
        Assert.AreEqual(2, versions.Count);
        Assert.AreEqual(firstId, versions[0].Id);
        Assert.AreEqual("Publication draft", versions[0].Title);
        Assert.AreEqual(180, versions[0].TimeLimit);
        Assert.IsFalse(versions[0].IsFree);
        Assert.AreEqual("Original question", versions[0].QuestionSnapshots.Single().Content);
        Assert.AreEqual("Updated draft", versions[1].Title);
        Assert.AreEqual(120, versions[1].TimeLimit);
        Assert.IsFalse(versions[1].IsFree);
        Assert.AreEqual("Updated question", versions[1].QuestionSnapshots.Single().Content);
        Assert.AreEqual(2, versions[1].Version);
        Assert.AreEqual(PaperStatus.Publishable, (await context.RealPapers.SingleAsync(p => p.Id == id)).Status);
        Assert.AreEqual(oldPaperCount, await context.Papers.CountAsync());
        var html = await Http.GetStringAsync($"/RealPapers/Version/{id}?snapshotId={firstId}");
        Assert.Contains("Original question", html);
        Assert.DoesNotContain("Updated question", html);
        Assert.Contains("Original answer", html);
    }

    private async Task<Guid> ReadInstitution(Guid id)
    {
        using var scope = Server!.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.SingleAsync(p => p.Id == id)).InstitutionId;
    }

    [TestMethod]
    public async Task EmptyDraftIsRejectedButCrossCourseQuestionsPublishTogether()
    {
        await LoginAsAdmin();
        var (id, question, _) = await Seed(false);
        var empty = await Publish(id);
        Assert.AreEqual(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Add at least one question", await empty.Content.ReadAsStringAsync());
        Guid otherQuestion;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var other = new Question { Content = "Other course", QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod() };
            db.Questions.Add(other); otherQuestion = other.Id;
            db.RealPaperQuestions.AddRange(
                new RealPaperQuestion { RealPaperId = id, QuestionId = question, Order = 1, Score = 10 },
                new RealPaperQuestion { RealPaperId = id, Question = other, Order = 2, Score = 20 });
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.Found, (await Publish(id)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var saved = await context.PaperSnapshots.Include(s => s.QuestionSnapshots).SingleAsync(s => s.RealPaperId == id);
        Assert.IsFalse(saved.IsFree);
        CollectionAssert.AreEquivalent(new[] { question, otherQuestion }, saved.QuestionSnapshots.Select(q => q.OriginalQuestionId!.Value).ToArray());
        Assert.AreEqual(30, saved.QuestionSnapshots.Sum(q => q.Score));
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
            var link = await db.RealPaperQuestions.SingleAsync(l => l.RealPaperId == id);
            link.Order = order; link.Score = score;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.OK, (await Publish(id)).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await context.PaperSnapshots.AnyAsync(s => s.RealPaperId == id));
        Assert.AreEqual(PaperStatus.Draft, (await context.RealPapers.SingleAsync(p => p.Id == id)).Status);
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
            { Id = Guid.NewGuid(), RealPaperId = id, Title = $"Saved draft {i:00}", Version = i }));
            var latest = db.ChangeTracker.Entries<PaperSnapshot>().Select(e => e.Entity).Single(s => s.Version == 25);
            snapshotId = latest.Id;
            db.QuestionSnapshots.AddRange(Enumerable.Range(1, 21).Select(i => new QuestionSnapshot
            { Id = Guid.NewGuid(), PaperSnapshotId = snapshotId, Order = i, Score = 10, Content = $"Saved question {i:00}" }));
            await db.SaveChangesAsync();
        }
        var first = await Http.GetStringAsync($"/RealPapers/Versions/{id}");
        Assert.Contains("Saved draft 25", first);
        Assert.DoesNotContain("Saved draft 01", first);
        var second = await Http.GetStringAsync($"/RealPapers/Versions/{id}?page=2");
        Assert.Contains("Saved draft 01", second);
        Assert.DoesNotContain("Saved draft 25", second);
        var page = await Http.GetStringAsync($"/RealPapers/Version/{id}?snapshotId={snapshotId}");
        Assert.Contains("Saved question 01", page);
        Assert.DoesNotContain("Saved question 21", page);
        var last = await Http.GetStringAsync($"/RealPapers/Version/{id}?snapshotId={snapshotId}&page=999");
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
            var other = new Institution { Title = "Another institution" };
            db.Institutions.Add(other);
            await db.SaveChangesAsync();
            otherCourse = other.Id;
            snapshotId = (await db.PaperSnapshots.SingleAsync(s => s.RealPaperId == id)).Id;
        }
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/RealPapers/RemoveQuestion/{id}", new Dictionary<string, string> { ["QuestionId"] = question.ToString() }, tokenUrl: $"/RealPapers/Questions/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/Questions/Delete/{question}", new Dictionary<string, string> { ["id"] = question.ToString() })).StatusCode);
        var rejected = await PostForm($"/RealPapers/Edit/{id}", new Dictionary<string, string>
        { ["Id"] = id.ToString(), ["Title"] = "Moved", ["InstitutionId"] = otherCourse.ToString(), ["TimeLimit"] = "120" });
        Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("The institution cannot change after", await rejected.Content.ReadAsStringAsync());
        Assert.AreEqual(course, await ReadInstitution(id));
        Assert.Contains("Original question", await Http.GetStringAsync($"/RealPapers/Version/{id}?snapshotId={snapshotId}"));
    }

    [TestMethod]
    public async Task PublishingAndAnswersRequireBothPaperAndQuestionPermissions()
    {
        var (id, _, _) = await Seed();
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/RealPapers/Versions/{id}")).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
            var user = await manager.FindByEmailAsync(email);
            await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(AppPermissions.Type, AppPermissionNames.CanEditPapers));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var denied = await Http.GetAsync($"/RealPapers/Versions/{id}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        denied = await PostForm($"/RealPapers/Publish/{id}", [], tokenUrl: $"/RealPapers/Edit/{id}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm($"/RealPapers/Publish/{id}", [], includeToken: false)).StatusCode);
        await Publish(id);
        using var verify = Server!.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var snapshot = await db.PaperSnapshots.SingleAsync(s => s.RealPaperId == id);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/RealPapers/Version/{Guid.NewGuid()}?snapshotId={snapshot.Id}")).StatusCode);
        (await db.RealPapers.SingleAsync(p => p.Id == id)).Status = PaperStatus.Frozen;
        await db.SaveChangesAsync();
        Assert.AreEqual(HttpStatusCode.Conflict, (await Publish(id)).StatusCode);
        Assert.AreEqual(1, await db.PaperSnapshots.CountAsync(s => s.RealPaperId == id));
    }

    [TestMethod]
    public async Task MigrationPreservesHistoricalSnapshotsAndRestrictsRealSources()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028095605_AddRealPaperQuestions");
        var legacy = new Paper { Title = "Legacy paper" };
        var course = new TotalCategory { Title = "Mock course" };
        var mock = new MockPaper { Title = "Mock paper", TotalCategory = course, TotalCategoryId = course.Id };
        var institution = new Institution { Title = "School" };
        var real = new RealPaper { Title = "Real paper", Institution = institution, InstitutionId = institution.Id };
        db.AddRange(legacy, mock, real);
        await db.SaveChangesAsync();
        var old = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PaperSnapshots (Id,PaperId,Version,Title,TimeLimit,IsFree,CreationTime) VALUES ({old},{legacy.Id},1,'Historical payload',180,0,CURRENT_TIMESTAMP)");
        await migrator.MigrateAsync();
        var retained = await db.PaperSnapshots.SingleAsync();
        Assert.AreEqual(old, retained.Id);
        Assert.AreEqual(legacy.Id, retained.PaperId);
        Assert.IsNull(retained.RealPaperId);
        Assert.AreEqual("Historical payload", retained.Title);
        db.PaperSnapshots.Add(new PaperSnapshot { Id = Guid.NewGuid(), RealPaperId = real.Id, Version = 1, Title = "New version" });
        await db.SaveChangesAsync();
        foreach (var invalid in new[]
        {
            new PaperSnapshot { Id = Guid.NewGuid(), RealPaperId = real.Id, Version = 1, Title = "Duplicate" },
            new PaperSnapshot { Id = Guid.NewGuid(), RealPaperId = real.Id, PaperId = legacy.Id, Version = 2, Title = "Two sources" },
            new PaperSnapshot { Id = Guid.NewGuid(), RealPaperId = real.Id, MockPaperId = mock.Id, Version = 2, Title = "Two new sources" },
            new PaperSnapshot { Id = Guid.NewGuid(), RealPaperId = Guid.NewGuid(), Version = 2, Title = "Unknown source" }
        })
        {
            db.ChangeTracker.Clear(); db.PaperSnapshots.Add(invalid);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.RealPapers.Where(p => p.Id == real.Id).ExecuteDeleteAsync());
        Assert.AreEqual(2, await db.PaperSnapshots.CountAsync());
    }
}
