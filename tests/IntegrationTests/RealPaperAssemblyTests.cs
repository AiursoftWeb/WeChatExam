using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class RealPaperAssemblyTests : TestBase
{
    private Guid _paper;
    private Guid _period;
    private Guid _otherPeriod;
    private Guid _question;
    private Guid _foreignQuestion;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        period.Title = "Included period";
        period.IsFree = false;
        var other = QuestionPeriodAssignmentTests.TestPeriod();
        other.Title = "Excluded period";
        var question = new Question { Id = Guid.NewGuid(), Content = "Included question", QuestionCategory = period };
        var foreign = new Question { Id = Guid.NewGuid(), Content = "Excluded question", QuestionCategory = other };
        var paper = new RealPaper { Title = "Assembly draft", Institution = new Institution { Title = "Cross-course institution" } };
        db.AddRange(question, foreign, paper);
        await db.SaveChangesAsync();
        (_paper, _period, _otherPeriod, _question, _foreignQuestion) =
            (paper.Id, period.Id, other.Id, question.Id, foreign.Id);
    }

    private Task<HttpResponseMessage> Add(params Guid[] ids) => PostForm($"/RealPapers/AddQuestions/{_paper}",
        ids.Select((id, i) => new KeyValuePair<string, string>($"QuestionIds[{i}]", id.ToString()))
            .Append(new KeyValuePair<string, string>("Score", "7")).ToDictionary(), tokenUrl: $"/RealPapers/Edit/{_paper}");

    [TestMethod]
    public async Task SelectionAllowsMultipleCoursesButRejectsMissingQuestionsAtomically()
    {
        await LoginAsAdmin();
        var html = await Http.GetStringAsync($"/RealPapers/SelectQuestions/{_paper}");
        Assert.Contains("Included question", html);
        Assert.Contains("Excluded question", html);
        Assert.Contains("Excluded period", html);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/RealPapers/SelectQuestions/{_paper}?questionCategoryId={_otherPeriod}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/RealPapers/SelectQuestions/{_paper}?questionCategoryId={Guid.NewGuid()}")).StatusCode);
        var invalid = await Add(_question, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Every selected question must exist", await invalid.Content.ReadAsStringAsync());
        using (var scope = Server!.Services.CreateScope())
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPaperQuestions.CountAsync(l => l.RealPaperId == _paper));
        Assert.AreEqual(HttpStatusCode.Found, (await Add(_question, _foreignQuestion, _question)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await Add(_question)).StatusCode);
        html = await Http.GetStringAsync($"/RealPapers/SelectQuestions/{_paper}");
        Assert.DoesNotContain("Included question", html);
        Assert.DoesNotContain("Excluded question", html);
        using var verify = Server!.Services.CreateScope();
        var links = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPaperQuestions.Where(l => l.RealPaperId == _paper).OrderBy(l => l.Order).ToListAsync();
        Assert.AreEqual(2, links.Count);
        CollectionAssert.AreEqual(new[] { 1, 2 }, links.Select(l => l.Order).ToArray());
        Assert.IsTrue(links.All(l => l.Score == 7));
    }

    [TestMethod]
    public async Task OrderScoreAndUnlinkingNeverChangeTheOriginalQuestion()
    {
        await LoginAsAdmin();
        await Add(_question);
        var form = new Dictionary<string, string> { ["QuestionId"] = _question.ToString(), ["Order"] = "3", ["Score"] = "15" };
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/RealPapers/UpdateQuestion/{_paper}", form, tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        foreach (var field in new[] { "Order", "Score" })
        {
            var invalid = new Dictionary<string, string>(form) { [field] = "-1" };
            Assert.AreEqual(HttpStatusCode.OK, (await PostForm($"/RealPapers/UpdateQuestion/{_paper}", invalid, tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        }
        using (var scope = Server!.Services.CreateScope())
        {
            var link = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPaperQuestions.SingleAsync(l => l.RealPaperId == _paper);
            Assert.AreEqual(3, link.Order);
            Assert.AreEqual(15, link.Score);
        }
        form["QuestionId"] = _foreignQuestion.ToString();
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/RealPapers/RemoveQuestion/{_paper}", form, tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        form["QuestionId"] = _question.ToString();
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/RealPapers/RemoveQuestion/{_paper}", form, tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await db.RealPaperQuestions.AnyAsync(l => l.RealPaperId == _paper));
        Assert.AreEqual("Included question", (await db.Questions.SingleAsync(q => q.Id == _question)).Content);
    }

    [TestMethod]
    public async Task RealPaperAllowsCourseMovesButPreventsSingleAndBatchQuestionDeletion()
    {
        await LoginAsAdmin();
        await Add(_question);
        var move = await PostForm($"/Questions/Period/{_question}", new Dictionary<string, string>
        { ["Id"] = _question.ToString(), ["QuestionCategoryId"] = _otherPeriod.ToString() });
        Assert.AreEqual(HttpStatusCode.Found, move.StatusCode);
        var deletion = await PostForm($"/Questions/Delete/{_question}", new Dictionary<string, string> { ["id"] = _question.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Contains("Assembly draft", await deletion.Content.ReadAsStringAsync());
        Assert.Contains("Remove it from the paper", await deletion.Content.ReadAsStringAsync());
        Http.DefaultRequestHeaders.Add("RequestVerificationToken", await GetAntiCsrfToken($"/RealPapers/Questions/{_paper}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync("/Questions/BatchDelete", new { QuestionIds = new[] { _question, _foreignQuestion } })).StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(_otherPeriod, (await db.Questions.SingleAsync(q => q.Id == _question)).QuestionCategoryId);
        Assert.AreEqual(2, await db.Questions.CountAsync(q => q.Id == _question || q.Id == _foreignQuestion));
        Assert.AreEqual(1, await db.RealPaperQuestions.CountAsync(l => l.RealPaperId == _paper));
    }

    [TestMethod]
    public async Task FrozenPapersAndRequestsWithoutPermissionOrAntiforgeryCannotAssemble()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/RealPapers/Questions/{_paper}")).StatusCode);
        await RegisterAndLoginAsync();
        var denied = await Http.GetAsync($"/RealPapers/SelectQuestions/{_paper}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm($"/RealPapers/AddQuestions/{_paper}", new Dictionary<string, string> { ["QuestionIds[0]"] = _question.ToString() }, includeToken: false)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.RealPapers.SingleAsync(p => p.Id == _paper)).Status = PaperStatus.Frozen;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.GetAsync($"/RealPapers/Questions/{_paper}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await PostForm($"/RealPapers/AddQuestions/{_paper}", new Dictionary<string, string> { ["QuestionIds[0]"] = _question.ToString() }, tokenUrl: "/")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        Assert.IsFalse(await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPaperQuestions.AnyAsync(l => l.RealPaperId == _paper));
    }

    [TestMethod]
    public async Task SelectionSearchAndPeriodRemainScopedAcrossPages()
    {
        await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Questions.AddRange(Enumerable.Range(0, 21).Select(i => new Question { Id = Guid.NewGuid(), Content = $"Page question {i:00}", QuestionCategoryId = _period, OrderIndex = i }));
            await db.SaveChangesAsync();
        }
        var url = $"/RealPapers/SelectQuestions/{_paper}?search=Page&questionCategoryId={_period}";
        var first = await Http.GetStringAsync(url);
        Assert.Contains("Page question 00", first);
        Assert.DoesNotContain("Page question 20", first);
        Assert.DoesNotContain("Excluded question", first);
        Assert.Contains($"questionCategoryId={_period}", first);
        var last = await Http.GetStringAsync(url + "&page=999");
        Assert.Contains("Page question 20", last);
        Assert.DoesNotContain("Page question 00", last);
    }

    [TestMethod]
    public async Task InvalidScoresEmptyBatchesAndOtherPaperLinksDoNotChangeAssembly()
    {
        await LoginAsAdmin();
        foreach (var form in new[]
                 {
                     new Dictionary<string, string> { ["Score"] = "10" },
                     new Dictionary<string, string> { ["QuestionIds[0]"] = _question.ToString(), ["Score"] = "0" }
                 })
            Assert.AreEqual(HttpStatusCode.OK, (await PostForm($"/RealPapers/AddQuestions/{_paper}", form,
                tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(0, await db.RealPaperQuestions.CountAsync(l => l.RealPaperId == _paper));
        var otherPaper = new RealPaper { Title = "Another institution paper", Institution = new Institution { Title = "Another institution" } };
        db.Add(otherPaper);
        db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaper = otherPaper, QuestionId = _foreignQuestion, Score = 20, Order = 4 });
        await db.SaveChangesAsync();
        var edit = new Dictionary<string, string> { ["QuestionId"] = _foreignQuestion.ToString(), ["Score"] = "1", ["Order"] = "0" };
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/RealPapers/UpdateQuestion/{_paper}", edit,
            tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/RealPapers/RemoveQuestion/{_paper}", edit,
            tokenUrl: $"/RealPapers/Edit/{_paper}")).StatusCode);
        await Add(_foreignQuestion);
        db.ChangeTracker.Clear();
        Assert.AreEqual(2, await db.RealPaperQuestions.CountAsync(l => l.QuestionId == _foreignQuestion));
        var original = await db.RealPaperQuestions.SingleAsync(l => l.RealPaperId == otherPaper.Id);
        Assert.AreEqual(20, original.Score);
        Assert.AreEqual(4, original.Order);
    }

    [TestMethod]
    public async Task RelationalUpgradePreservesDraftsAndProtectsSelectedQuestions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028091740_AddRealPapers");
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var question = new Question { Id = Guid.NewGuid(), Content = "Retained question", QuestionCategory = period };
        var paper = new RealPaper { Title = "Retained draft", Institution = new Institution { Title = "Retained institution" } };
        db.AddRange(question, paper);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.AreEqual("Retained draft", (await db.RealPapers.SingleAsync()).Title);
        db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaperId = paper.Id, QuestionId = question.Id, Order = 2, Score = 9 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.Questions.Where(q => q.Id == question.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.RealPapers.Where(p => p.Id == paper.Id).ExecuteDeleteAsync());
        db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaperId = paper.Id, QuestionId = question.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var saved = await db.RealPaperQuestions.SingleAsync();
        Assert.AreEqual(2, saved.Order);
        Assert.AreEqual(9, saved.Score);
        await db.RealPaperQuestions.ExecuteDeleteAsync();
        Assert.AreEqual(1, await db.Questions.CountAsync());
    }
}
