using System.Net;
using Aiursoft.WeChatExam.Controllers.Management;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Models.TestPapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.WeChatExam.Tests.TestServices;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TestPapersTests : TestBase
{
    [TestMethod]
    [DataRow(TestPaperKind.Mock)]
    [DataRow(TestPaperKind.Real)]
    public async Task AdministratorCanTakeAndGradePaperWithoutCreatingStudentHistory(TestPaperKind kind)
    {
        await LoginAsAdmin();

        var paperId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var paperTitle = $"Browser test {Guid.NewGuid()}";
        var questionContent = $"Choose the correct answer {Guid.NewGuid()}";

        using (var scope = Server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var question = new Question
            {
                QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
                Id = questionId,
                Content = questionContent,
                QuestionType = QuestionType.Choice,
                GradingStrategy = GradingStrategy.ExactMatch,
                Metadata = "{\"options\":[\"Mozart\",\"Bach\"]}",
                StandardAnswer = "Bach",
                Explanation = "Bach is the expected answer."
            };
            AddPaper(context, kind, paperId, paperTitle, question, 10);
            await context.SaveChangesAsync();
        }

        var indexResponse = await Http.GetAsync($"/TestPapers?kind={kind}");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains(paperTitle, indexHtml);
        Assert.Contains("Start test", indexHtml);

        var takeUrl = $"/TestPapers/Take/{paperId}?kind={kind}";
        var takeResponse = await Http.GetAsync(takeUrl);
        takeResponse.EnsureSuccessStatusCode();
        var takeHtml = await takeResponse.Content.ReadAsStringAsync();
        Assert.Contains(questionContent, takeHtml);
        Assert.Contains("Real AI grading", takeHtml);
        Assert.Contains($"Answers[{questionId}]", takeHtml);

        int examRecordsBefore;
        int practiceRecordsBefore;
        using (var scope = Server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            examRecordsBefore = await context.ExamRecords.CountAsync();
            practiceRecordsBefore = await context.UserPracticeHistories.CountAsync();
        }

        var submitResponse = await PostForm("/TestPapers/Submit", new Dictionary<string, string>
        {
            ["PaperId"] = paperId.ToString(),
            ["Kind"] = kind.ToString(),
            [$"Answers[{questionId}]"] = "Bach"
        }, tokenUrl: takeUrl);
        submitResponse.EnsureSuccessStatusCode();
        var resultHtml = await submitResponse.Content.ReadAsStringAsync();
        Assert.Contains("Grading complete", resultHtml);
        Assert.Contains("10<small>/10</small>", resultHtml);
        Assert.Contains("Bach is the expected answer.", resultHtml);

        using (var scope = Server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(examRecordsBefore, await context.ExamRecords.CountAsync());
            Assert.AreEqual(practiceRecordsBefore, await context.UserPracticeHistories.CountAsync());
        }

        var legacyResponse = await Http.GetAsync("/JoinExam");
        Assert.AreEqual(HttpStatusCode.Found, legacyResponse.StatusCode);
        Assert.AreEqual("/TestPapers", legacyResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    [DataRow(TestPaperKind.Mock)]
    [DataRow(TestPaperKind.Real)]
    public async Task AiEvalPaperInvokesSharedGradingService(TestPaperKind kind)
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options);
        var gradingService = new StubGradingService();

        var paperId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var question = new Question
        {
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
            Id = questionId,
            Content = "Discuss the musical significance.",
            QuestionType = QuestionType.Essay,
            GradingStrategy = GradingStrategy.AiEval,
            StandardAnswer = "A structured standard answer",
            Explanation = "A detailed explanation"
        };
        AddPaper(context, kind, paperId, "AI paper", question, 20);
        await context.SaveChangesAsync();

        gradingService.Grade = (answer, standardAnswer, strategy, maxScore, content) =>
        {
            Assert.AreEqual("A thoughtful student answer", answer);
            Assert.AreEqual(question.StandardAnswer, standardAnswer);
            Assert.AreEqual(GradingStrategy.AiEval, strategy);
            Assert.AreEqual(20, maxScore);
            Assert.AreEqual(question.Content, content);
            return Task.FromResult(new GradingResult { IsCorrect = true, Score = 17, Comment = "Strong answer" });
        };

        var controller = new TestPapersController(context, gradingService);
        var actionResult = await controller.Submit(new SubmitViewModel
        {
            PaperId = paperId, Kind = kind,
            Answers = new Dictionary<Guid, string> { [questionId] = "A thoughtful student answer" }
        });

        var view = actionResult as ViewResult;
        Assert.IsNotNull(view);
        Assert.AreEqual("Result", view.ViewName);
        var result = view.Model as ResultViewModel;
        Assert.IsNotNull(result);
        Assert.AreEqual(17, result.Score);
        Assert.AreEqual("Strong answer", result.Questions.Single().Comment);
        Assert.AreEqual(1, gradingService.Calls);
    }

    [TestMethod]
    public async Task IdenticalIdsUseSelectedKindAndIgnoreLegacyPaperAndForeignAnswers()
    {
        await LoginAsAdmin();
        var id = Guid.NewGuid();
        var mockQuestion = NewQuestion("Mock current question");
        var realQuestion = NewQuestion("Real current question");
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            AddPaper(db, TestPaperKind.Mock, id, "Mock current", mockQuestion, 7);
            AddPaper(db, TestPaperKind.Real, id, "Real current", realQuestion, 23);
            db.Papers.Add(new Paper { Id = id, Title = "Legacy must not be used" });
            await db.SaveChangesAsync();
        }
        foreach (var kind in new[] { TestPaperKind.Mock, TestPaperKind.Real })
        {
            var url = $"/TestPapers/Take/{id}?kind={kind}";
            var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains($"{kind} current question", html);
            Assert.DoesNotContain(kind == TestPaperKind.Mock ? realQuestion.Content : mockQuestion.Content, html);
            Assert.DoesNotContain("Legacy must not be used", html);
            var grade = await PostForm("/TestPapers/Submit", new Dictionary<string, string>
            {
                ["PaperId"] = id.ToString(), ["Kind"] = kind.ToString(),
                [$"Answers[{mockQuestion.Id}]"] = "Bach", [$"Answers[{realQuestion.Id}]"] = "Bach"
            }, tokenUrl: url);
            grade.EnsureSuccessStatusCode();
            var result = await grade.Content.ReadAsStringAsync();
            Assert.Contains(kind == TestPaperKind.Mock ? "7<small>/7</small>" : "23<small>/23</small>", result);
            Assert.Contains($"data-paper-kind=\"{kind}\"", result);
        }
    }

    [TestMethod]
    [DataRow(TestPaperKind.Mock)]
    [DataRow(TestPaperKind.Real)]
    public async Task OrderedCurrentQuestionsKeepOptionsAndPerPaperScores(TestPaperKind kind)
    {
        await using var db = new InMemoryContext(new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var id = Guid.NewGuid();
        var first = NewQuestion("First");
        var last = NewQuestion("Last");
        last.QuestionType = QuestionType.Bool;
        last.Metadata = "invalid old metadata";
        last.StandardAnswer = "true";
        AddPaper(db, kind, id, "Ordered template", last, 20);
        if (kind == TestPaperKind.Mock)
            db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaperId = id, Question = first, Order = -1, Score = 7 });
        else
            db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaperId = id, Question = first, Order = -1, Score = 7 });
        await db.SaveChangesAsync();
        // A correction to the live question must appear without republishing a snapshot.
        first.Content = "First corrected";
        await db.SaveChangesAsync();
        var grading = new StubGradingService { Grade = (_, _, _, max, _) => Task.FromResult(new GradingResult { Score = max }) };
        var controller = new TestPapersController(db, grading);
        var take = (TakeViewModel)((ViewResult)await controller.Take(id, kind)).Model!;
        Assert.AreEqual(27, take.TotalScore);
        Assert.AreEqual("First corrected", take.Questions[0].Content);
        CollectionAssert.AreEqual(new[] { "Mozart", "Bach" }, take.Questions[0].Options);
        CollectionAssert.AreEqual(new[] { "True", "False" }, take.Questions[1].Options);
        var result = (ResultViewModel)((ViewResult)await controller.Submit(new SubmitViewModel
        {
            PaperId = id, Kind = kind,
            Answers = new Dictionary<Guid, string> { [first.Id] = "Bach", [Guid.NewGuid()] = "Unrelated" }
        })).Model!;
        Assert.AreEqual(2, grading.Calls);
        Assert.AreEqual(1, result.AnsweredCount);
        Assert.AreEqual(7, result.Questions[0].MaxScore);
        Assert.AreEqual(20, result.Questions[1].MaxScore);
        Assert.AreEqual(string.Empty, result.Questions[1].UserAnswer);
        Assert.AreEqual(0, await db.PaperSnapshots.CountAsync());
    }

    [TestMethod]
    public async Task ListFiltersBeforePaginationAndKeepsKindsSeparate()
    {
        await LoginAsAdmin();
        var prefix = $"Pagination-{Guid.NewGuid()}";
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var course = new TotalCategory { Title = "Test course" };
            for (var i = 0; i < 26; i++)
                db.MockPapers.Add(new MockPaper
                {
                    Title = $"{prefix}-{i:D2}", TotalCategory = course,
                    CreationTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-i)
                });
            db.MockPapers.Add(new MockPaper { Title = prefix + "-hidden-status", TotalCategory = course, Status = PaperStatus.Frozen });
            db.RealPapers.Add(new RealPaper { Title = prefix + "-other-kind", Institution = new Institution { Title = "School" } });
            await db.SaveChangesAsync();
        }
        var first = await Http.GetStringAsync($"/TestPapers?kind=Mock&search={prefix}&status=Draft");
        var second = await Http.GetStringAsync($"/TestPapers?kind=Mock&search={prefix}&status=Draft&page=999");
        Assert.Contains(prefix + "-00", first);
        Assert.Contains(prefix + "-23", first);
        Assert.DoesNotContain(prefix + "-24", first);
        Assert.Contains("Page 1 of 2", first);
        Assert.Contains("Page 2 of 2", second);
        Assert.Contains(prefix + "-24", second);
        Assert.Contains(prefix + "-25", second);
        Assert.DoesNotContain(prefix + "-23", second);
        Assert.DoesNotContain(prefix + "-hidden-status", first + second);
        Assert.DoesNotContain(prefix + "-other-kind", first + second);
        Assert.Contains("status=Draft", second);
        Assert.Contains("kind=Mock", second);
        var real = await Http.GetStringAsync($"/TestPapers?kind=Real&search={prefix}");
        Assert.Contains(prefix + "-other-kind", real);
        Assert.DoesNotContain(prefix + "-00", real);
    }

    [TestMethod]
    public async Task InvalidKindsMissingPapersAndEmptyPapersDoNotFallBack()
    {
        await LoginAsAdmin();
        var id = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.RealPapers.Add(new RealPaper { Id = id, Title = "Empty real paper", Institution = new Institution { Title = "School" } });
            db.Papers.Add(new Paper { Id = legacyId, Title = "Legacy only" });
            await db.SaveChangesAsync();
        }
        foreach (var query in new[] { "", "?kind=99", "?kind=invalid" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/TestPapers/Take/{id}{query}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/TestPapers/Take/{id}?kind=Mock")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/TestPapers/Take/{legacyId}?kind=Real")).StatusCode);
        AssertRedirect(await Http.GetAsync($"/TestPapers/Take/{id}?kind=Real"), "/TestPapers?kind=Real");
        foreach (var kind in new[] { "", "invalid", "99", "Mock", "Real" })
        {
            var response = await PostForm("/TestPapers/Submit", new Dictionary<string, string>
            {
                ["PaperId"] = id.ToString(), ["Kind"] = kind
            }, tokenUrl: "/Account/Login");
            if (kind == "Real") AssertRedirect(response, "/TestPapers?kind=Real");
            else Assert.AreEqual(kind == "Mock" ? HttpStatusCode.NotFound : HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync("/TestPapers?kind=invalid")).StatusCode);
    }

    [TestMethod]
    public async Task TestActionsRequireManagementPermissionAndAntiforgery()
    {
        var id = Guid.NewGuid();
        foreach (var url in new[] { "/TestPapers", $"/TestPapers/Take/{id}?kind=Mock" })
            AssertRedirect(await Http.GetAsync(url), "/Account/Login", exact: false);
        AssertRedirect(await Http.PostAsync("/TestPapers/Submit", new FormUrlEncodedContent(new Dictionary<string, string>())),
            "/Account/Login", exact: false);
        await RegisterAndLoginAsync();
        AssertRedirect(await Http.GetAsync("/TestPapers?kind=Real"), "/Error/Unauthorized", exact: false);
        AssertRedirect(await Http.GetAsync($"/TestPapers/Take/{id}?kind=Real"), "/Error/Unauthorized", exact: false);
        AssertRedirect(await Http.PostAsync("/TestPapers/Submit", new FormUrlEncodedContent(new Dictionary<string, string>())),
            "/Error/Unauthorized", exact: false);
        await LoginAsAdmin();
        Assert.AreEqual(HttpStatusCode.BadRequest, (await PostForm("/TestPapers/Submit", new Dictionary<string, string>
        {
            ["PaperId"] = id.ToString(), ["Kind"] = "Real"
        }, includeToken: false)).StatusCode);
    }

    private static Question NewQuestion(string content) => new()
    {
        Content = content, QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
        QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch,
        StandardAnswer = "Bach", Metadata = "{\"options\":[\"Mozart\",\"Bach\"]}"
    };

    private static void AddPaper(WeChatExamDbContext context, TestPaperKind kind, Guid id, string title, Question question, int score)
    {
        if (kind == TestPaperKind.Mock)
        {
            var paper = new MockPaper { Id = id, Title = title, TotalCategory = question.QuestionCategory!.TotalCategory!, TimeLimit = 30 };
            context.Add(new MockPaperQuestion { MockPaper = paper, Question = question, Order = 1, Score = score });
        }
        else
        {
            var paper = new RealPaper { Id = id, Title = title, Institution = new Institution { Title = "Test school" }, TimeLimit = 30 };
            context.Add(new RealPaperQuestion { RealPaper = paper, Question = question, Order = 1, Score = score });
        }
    }
}
