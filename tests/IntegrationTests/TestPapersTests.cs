using System.Net;
using Aiursoft.WeChatExam.Controllers.Management;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Models.TestPapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TestPapersTests : TestBase
{
    [TestMethod]
    public async Task AdministratorCanTakeAndGradePaperWithoutCreatingStudentHistory()
    {
        await LoginAsAdmin();

        var paperId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var paperTitle = $"Browser test {Guid.NewGuid()}";
        var questionContent = $"Choose the correct answer {Guid.NewGuid()}";

        using (var scope = Server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = new Paper
            {
                Id = paperId,
                Title = paperTitle,
                TimeLimit = 30,
                Status = PaperStatus.Draft
            };
            var question = new Question
            {
                Id = questionId,
                Content = questionContent,
                QuestionType = QuestionType.Choice,
                GradingStrategy = GradingStrategy.ExactMatch,
                Metadata = "{\"options\":[\"Mozart\",\"Bach\"]}",
                StandardAnswer = "Bach",
                Explanation = "Bach is the expected answer."
            };
            context.AddRange(paper, question, new PaperQuestion
            {
                PaperId = paperId,
                Paper = paper,
                QuestionId = questionId,
                Question = question,
                Order = 1,
                Score = 10
            });
            await context.SaveChangesAsync();
        }

        var indexResponse = await Http.GetAsync("/TestPapers");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains(paperTitle, indexHtml);
        Assert.Contains("Start test", indexHtml);

        var takeUrl = $"/TestPapers/Take/{paperId}";
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
    public async Task AiEvalPaperInvokesSharedGradingService()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new InMemoryContext(options);
        var gradingService = new Mock<IGradingService>();

        var paperId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var paper = new Paper { Id = paperId, Title = "AI paper", TimeLimit = 60 };
        var question = new Question
        {
            Id = questionId,
            Content = "Discuss the musical significance.",
            QuestionType = QuestionType.Essay,
            GradingStrategy = GradingStrategy.AiEval,
            StandardAnswer = "A structured standard answer",
            Explanation = "A detailed explanation"
        };
        context.AddRange(paper, question, new PaperQuestion
        {
            PaperId = paperId,
            Paper = paper,
            QuestionId = questionId,
            Question = question,
            Order = 1,
            Score = 20
        });
        await context.SaveChangesAsync();

        gradingService
            .Setup(service => service.GradeAsync(
                "A thoughtful student answer",
                question.StandardAnswer,
                GradingStrategy.AiEval,
                20,
                question.Content))
            .ReturnsAsync(new GradingResult { IsCorrect = true, Score = 17, Comment = "Strong answer" });

        var controller = new TestPapersController(context, gradingService.Object);
        var actionResult = await controller.Submit(new SubmitViewModel
        {
            PaperId = paperId,
            Answers = new Dictionary<Guid, string> { [questionId] = "A thoughtful student answer" }
        });

        var view = actionResult as ViewResult;
        Assert.IsNotNull(view);
        Assert.AreEqual("Result", view.ViewName);
        var result = view.Model as ResultViewModel;
        Assert.IsNotNull(result);
        Assert.AreEqual(17, result.Score);
        Assert.AreEqual("Strong answer", result.Questions.Single().Comment);
        gradingService.VerifyAll();
    }
}
