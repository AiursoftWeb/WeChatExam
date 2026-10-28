using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class MockPaperAssemblyTests : TestBase
{
    private Guid _paper;
    private Guid _period;
    private Guid _otherPeriod;
    private Guid _question;
    private Guid _foreignQuestion;
    private Guid _course;
    private Guid _otherCourse;

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
        var paper = new MockPaper { Title = "Assembly draft", TotalCategory = period.TotalCategory!, TotalCategoryId = period.TotalCategoryId, AllowViewAnonymousely = true };
        db.AddRange(question, foreign, paper);
        await db.SaveChangesAsync();
        (_paper, _period, _otherPeriod, _question, _foreignQuestion, _course, _otherCourse) =
            (paper.Id, period.Id, other.Id, question.Id, foreign.Id, period.TotalCategoryId, other.TotalCategoryId);
    }

    private Task<HttpResponseMessage> Add(params Guid[] ids) => PostForm($"/MockPapers/AddQuestions/{_paper}",
        ids.Select((id, i) => new KeyValuePair<string, string>($"QuestionIds[{i}]", id.ToString()))
            .Append(new KeyValuePair<string, string>("Score", "7")).ToDictionary(), tokenUrl: $"/MockPapers/Edit/{_paper}");

    [TestMethod]
    public async Task SelectionIsCourseScopedAndInvalidBatchesDoNotPartiallyAdd()
    {
        await LoginAsAdmin();
        var html = await Http.GetStringAsync($"/MockPapers/SelectQuestions/{_paper}");
        Assert.Contains("Included question", html);
        Assert.DoesNotContain("Excluded question", html);
        Assert.DoesNotContain("Excluded period", html);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/MockPapers/SelectQuestions/{_paper}?questionCategoryId={_otherPeriod}")).StatusCode);
        foreach (var invalid in new[] { _foreignQuestion, Guid.NewGuid() })
        {
            var result = await Add(_question, invalid);
            Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);
            Assert.Contains("Every selected question must exist", await result.Content.ReadAsStringAsync());
        }
        using (var scope = Server!.Services.CreateScope())
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPaperQuestions.CountAsync(l => l.MockPaperId == _paper));
        // Explicitly free mock papers may contain paid questions; selection is based on course, not pricing.
        Assert.AreEqual(HttpStatusCode.Found, (await Add(_question, _question)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Found, (await Add(_question)).StatusCode);
        html = await Http.GetStringAsync($"/MockPapers/SelectQuestions/{_paper}");
        Assert.DoesNotContain("Included question", html);
        using var verify = Server!.Services.CreateScope();
        var link = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPaperQuestions.SingleAsync(l => l.MockPaperId == _paper);
        Assert.AreEqual(_question, link.QuestionId);
        Assert.AreEqual(7, link.Score);
        Assert.AreEqual(1, link.Order);
    }

    [TestMethod]
    public async Task OrderScoreAndUnlinkingNeverChangeTheOriginalQuestion()
    {
        await LoginAsAdmin();
        await Add(_question);
        var form = new Dictionary<string, string> { ["QuestionId"] = _question.ToString(), ["Order"] = "3", ["Score"] = "15" };
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/MockPapers/UpdateQuestion/{_paper}", form, tokenUrl: $"/MockPapers/Questions/{_paper}")).StatusCode);
        foreach (var field in new[] { "Order", "Score" })
        {
            var invalid = new Dictionary<string, string>(form) { [field] = "-1" };
            Assert.AreEqual(HttpStatusCode.OK, (await PostForm($"/MockPapers/UpdateQuestion/{_paper}", invalid, tokenUrl: $"/MockPapers/Questions/{_paper}")).StatusCode);
        }
        using (var scope = Server!.Services.CreateScope())
        {
            var link = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPaperQuestions.SingleAsync(l => l.MockPaperId == _paper);
            Assert.AreEqual(3, link.Order);
            Assert.AreEqual(15, link.Score);
        }
        form["QuestionId"] = _foreignQuestion.ToString();
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostForm($"/MockPapers/RemoveQuestion/{_paper}", form, tokenUrl: $"/MockPapers/Questions/{_paper}")).StatusCode);
        form["QuestionId"] = _question.ToString();
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm($"/MockPapers/RemoveQuestion/{_paper}", form, tokenUrl: $"/MockPapers/Questions/{_paper}")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await db.MockPaperQuestions.AnyAsync(l => l.MockPaperId == _paper));
        Assert.AreEqual("Included question", (await db.Questions.SingleAsync(q => q.Id == _question)).Content);
    }

    [TestMethod]
    public async Task ExistingAssemblyBlocksCourseMovesAndQuestionDeletion()
    {
        await LoginAsAdmin();
        await Add(_question);
        var move = await PostForm($"/Questions/Period/{_question}", new Dictionary<string, string>
        { ["Id"] = _question.ToString(), ["QuestionCategoryId"] = _otherPeriod.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, move.StatusCode);
        Assert.Contains("Assembly draft", await move.Content.ReadAsStringAsync());
        var edit = await PostForm($"/MockPapers/Edit/{_paper}", new Dictionary<string, string>
        { ["Id"] = _paper.ToString(), ["Title"] = "Moved draft", ["TotalCategoryId"] = _otherCourse.ToString(), ["TimeLimit"] = "180" });
        Assert.AreEqual(HttpStatusCode.OK, edit.StatusCode);
        var deletion = await PostForm($"/Questions/Delete/{_question}", new Dictionary<string, string> { ["id"] = _question.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, deletion.StatusCode);
        Assert.Contains("Remove it from the paper", await deletion.Content.ReadAsStringAsync());
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(_period, (await db.Questions.SingleAsync(q => q.Id == _question)).QuestionCategoryId);
        Assert.AreEqual(_course, (await db.MockPapers.SingleAsync(p => p.Id == _paper)).TotalCategoryId);
        var validation = scope.ServiceProvider.GetRequiredService<MockPaperValidation>();
        Assert.IsNotNull(await validation.ValidatePeriodCourseAsync(_period, _otherCourse));
        Assert.IsNull(await validation.ValidatePeriodCourseAsync(_period, _course));
        Assert.IsNull(await validation.ValidateQuestionPeriodsAsync(new Dictionary<Guid, Guid> { [_question] = _period }));
        Assert.IsNotNull(await validation.ValidateQuestionPeriodsAsync(new Dictionary<Guid, Guid> { [_question] = _otherPeriod, [_foreignQuestion] = _otherPeriod }));
        db.MockPaperQuestions.Remove(await db.MockPaperQuestions.SingleAsync(l => l.MockPaperId == _paper));
        await db.SaveChangesAsync();
        Assert.IsNull(await validation.ValidatePeriodCourseAsync(_period, _otherCourse));
        Assert.IsNull(await validation.ValidateQuestionPeriodsAsync(new Dictionary<Guid, Guid> { [_question] = _otherPeriod }));
        Assert.IsNull(await validation.ValidateDeletionAsync([_question]));
    }

    [TestMethod]
    public async Task AllEditingRoutesPreserveExistingAssemblyAndBatchDeletionIsAtomic()
    {
        await LoginAsAdmin();
        await Add(_question);
        var periodEdit = await PostForm($"/QuestionCategories/Edit/{_period}", new Dictionary<string, string>
        { ["Id"] = _period.ToString(), ["Title"] = "Moved period", ["TotalCategoryId"] = _otherCourse.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, periodEdit.StatusCode);
        Assert.Contains("Assembly draft", await periodEdit.Content.ReadAsStringAsync());
        var questionEdit = await PostForm($"/Questions/Edit/{_question}", new Dictionary<string, string>
        {
            ["Id"] = _question.ToString(), ["Content"] = "Must not save", ["QuestionCategoryId"] = _otherPeriod.ToString(),
            ["QuestionType"] = "1", ["GradingStrategy"] = "0", ["StandardAnswer"] = "Answer"
        });
        Assert.AreEqual(HttpStatusCode.OK, questionEdit.StatusCode);
        Assert.Contains("Assembly draft", await questionEdit.Content.ReadAsStringAsync());
        using var scope = Server!.Services.CreateScope();
        var tasks = scope.ServiceProvider.GetRequiredService<AiTaskService>();
        var task = tasks.CreateTask([
            new AiTaskItem { QuestionId = _question, NewEntityId = _otherPeriod, Status = AiTaskStatus.Completed },
            new AiTaskItem { QuestionId = _foreignQuestion, NewEntityId = _period, Status = AiTaskStatus.Completed }
        ], AiTaskType.AutoCategorize);
        Http.DefaultRequestHeaders.Add("RequestVerificationToken", await GetAntiCsrfToken($"/AiTasks/Preview?taskId={task.Id}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/AiTasks/Adopt?taskId={task.Id}&questionId={_question}", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsync($"/AiTasks/AdoptAll?taskId={task.Id}", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync($"/AiTasks/Edit?taskId={task.Id}&questionId={_question}", _otherPeriod.ToString())).StatusCode);
        Assert.AreEqual(2, task.Items.Count);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync("/Questions/BatchDelete", new { QuestionIds = new[] { _question, _foreignQuestion } })).StatusCode);
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(_course, (await db.QuestionCategories.SingleAsync(c => c.Id == _period)).TotalCategoryId);
        Assert.AreEqual(_period, (await db.Questions.SingleAsync(q => q.Id == _question)).QuestionCategoryId);
        Assert.AreEqual("Included question", (await db.Questions.SingleAsync(q => q.Id == _question)).Content);
        Assert.AreEqual(_otherPeriod, (await db.Questions.SingleAsync(q => q.Id == _foreignQuestion)).QuestionCategoryId);
    }

    [TestMethod]
    public async Task FrozenPapersAndRequestsWithoutPermissionOrAntiforgeryCannotAssemble()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/MockPapers/Questions/{_paper}")).StatusCode);
        await RegisterAndLoginAsync();
        var denied = await Http.GetAsync($"/MockPapers/SelectQuestions/{_paper}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm($"/MockPapers/AddQuestions/{_paper}", new Dictionary<string, string> { ["QuestionIds[0]"] = _question.ToString() }, includeToken: false)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.MockPapers.SingleAsync(p => p.Id == _paper)).Status = PaperStatus.Frozen;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.GetAsync($"/MockPapers/Questions/{_paper}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await PostForm($"/MockPapers/AddQuestions/{_paper}", new Dictionary<string, string> { ["QuestionIds[0]"] = _question.ToString() }, tokenUrl: "/")).StatusCode);
        using var verify = Server!.Services.CreateScope();
        Assert.IsFalse(await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().MockPaperQuestions.AnyAsync(l => l.MockPaperId == _paper));
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
        var url = $"/MockPapers/SelectQuestions/{_paper}?search=Page&questionCategoryId={_period}";
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
    public async Task RelationalUpgradePreservesDraftsAndProtectsSelectedQuestions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027203016_AddMockPapers");
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var question = new Question { Id = Guid.NewGuid(), Content = "Retained question", QuestionCategory = period };
        var paper = new MockPaper { Title = "Retained draft", TotalCategory = period.TotalCategory!, TotalCategoryId = period.TotalCategoryId };
        db.AddRange(question, paper);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.AreEqual("Retained draft", (await db.MockPapers.SingleAsync()).Title);
        db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaperId = paper.Id, QuestionId = question.Id, Order = 2, Score = 9 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.Questions.Where(q => q.Id == question.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<SqliteException>(() => db.MockPapers.Where(p => p.Id == paper.Id).ExecuteDeleteAsync());
        db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaperId = paper.Id, QuestionId = question.Id });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var saved = await db.MockPaperQuestions.SingleAsync();
        Assert.AreEqual(2, saved.Order);
        Assert.AreEqual(9, saved.Score);
        await db.MockPaperQuestions.ExecuteDeleteAsync();
        Assert.AreEqual(1, await db.Questions.CountAsync());
    }
}
