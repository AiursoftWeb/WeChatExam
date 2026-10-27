using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public class ExtractTests : TestBase
{
    private Guid _category;
    private Guid _period;
    private readonly StubOllamaService _ai = new();
    private readonly string _questionText = "Extracted question " + Guid.NewGuid();
    private readonly string _lessonTitle = "Extracted lesson " + Guid.NewGuid();
    private readonly string _tagName = "Source tag " + Guid.NewGuid();
    private List<ExtractedKnowledgePoint> Data() =>
    [
        new()
        {
            KnowledgeTitle = _lessonTitle, KnowledgeContent = "Original lesson content",
            Questions = [new() { QuestionContent = _questionText, QuestionType = QuestionType.Choice,
                Metadata = ["A", "B"], StandardAnswer = "B", Explanation = "Original explanation", Tags = [_tagName] }]
        }
    ];

    [TestInitialize]
    public override async Task CreateServer()
    {
        _ai.Response = JsonConvert.SerializeObject(Data());
        TestStartupWithMockWeChat.MockOllamaService = _ai;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<TestStartupWithMockWeChat>(["--applicationName", "Aiursoft.WeChatExam"], port: reservation.Port);
        reservation.ConfigureServer(Server);
        await Server.UpdateDbAsync<WeChatExamDbContext>();
        await Server.SeedAsync();
        await Server.StartAsync();
        _period = await QuestionPeriodAssignmentTests.AddPeriod(Server.Services);
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new KnowledgeCategory { Title = "Knowledge directory", TotalCategoryId = Guid.NewGuid(), TotalCategory = new TotalCategory { Title = "Knowledge course" } };
        var existing = new KnowledgePoint { Title = "Existing lesson", Content = "Keep this" };
        db.KnowledgeCategoryKnowledgePoints.Add(new KnowledgeCategoryKnowledgePoint { KnowledgeCategory = category, KnowledgeCategoryId = category.Id,
            KnowledgePoint = existing, KnowledgePointId = existing.Id, OrderIndex = 7 });
        await db.SaveChangesAsync();
        _category = category.Id;
        await LoginAsAdmin();
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        await base.CleanServer();
        TestStartupWithMockWeChat.MockOllamaService = null;
        Http.Dispose();
    }

    private Dictionary<string, string> Form() => new()
    {
        ["KnowledgeCategoryId"] = _category.ToString(), ["QuestionCategoryId"] = _period.ToString(),
        ["JsonContent"] = JsonConvert.SerializeObject(Data()), ["OriginalMaterial"] = "Original source"
    };

    [TestMethod]
    public async Task PreviewSaveKeepsKnowledgePlacementAndAssignsQuestionsToPeriod()
    {
        var index = await Http.GetStringAsync("/Extract");
        Assert.DoesNotContain("field-validation-error", index);
        var preview = await PostForm("/Extract/Preview", Form(), "/Extract");
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        var html = await preview.Content.ReadAsStringAsync();
        Assert.Contains("Knowledge directory", html);
        Assert.Contains("Test music course", html);
        Assert.Contains("formaction=\"/Extract/ResumeEdit\"", html);
        AssertRedirect(await PostForm("/Extract/Submit", Form(), "/Extract"), "/Extract");
        await AssertSaved();
    }

    private async Task AssertSaved()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var q = await db.Questions.Include(q => q.QuestionTags).ThenInclude(t => t.Tag)
            .SingleAsync(q => q.Content == _questionText);
        Assert.AreEqual(_period, q.QuestionCategoryId);
        Assert.IsNull(q.CategoryId);
        Assert.AreEqual("B", q.StandardAnswer);
        Assert.AreEqual("Original explanation", q.Explanation);
        CollectionAssert.AreEqual(new[] { "A", "B" }, JsonConvert.DeserializeObject<string[]>(q.Metadata)!);
        Assert.AreEqual(_tagName, q.QuestionTags.Single().Tag.DisplayName);
        var lesson = await db.KnowledgePoints.SingleAsync(k => k.Title == _lessonTitle);
        Assert.AreEqual("Original lesson content", lesson.Content);
        var placement = await db.KnowledgeCategoryKnowledgePoints.SingleAsync(k => k.KnowledgePointId == lesson.Id);
        Assert.AreEqual(_category, placement.KnowledgeCategoryId);
        Assert.AreEqual(8, placement.OrderIndex);
        Assert.IsTrue(await db.KnowledgePointQuestions.AnyAsync(k => k.KnowledgePointId == lesson.Id && k.QuestionId == q.Id));
    }

    [TestMethod]
    public async Task BackgroundExtractionUsesExplicitPeriodAndConfiguredPrompt()
    {
        var form = Form();
        form["Material"] = "Source for background extraction";
        AssertRedirect(await PostForm("/Extract/Extract", form, "/Extract"), "/Jobs");
        for (var i = 0; i < 100; i++)
        {
            using var scope = Server!.Services.CreateScope();
            if (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Questions.AnyAsync(q => q.Content == _questionText))
            {
                await AssertSaved();
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Queued extraction did not save the generated question.");
    }

    [TestMethod]
    public async Task InvalidDestinationsDoNotSaveAndPreserveMaterialAndChoices()
    {
        foreach (var field in new[] { "KnowledgeCategoryId", "QuestionCategoryId" })
        foreach (var invalid in new[] { "", "invalid", Guid.Empty.ToString(), Guid.NewGuid().ToString() })
        foreach (var action in new[] { "Submit", "Extract" })
        {
            var form = Form();
            form[field] = invalid;
            form["Material"] = "Retain source";
            var response = await PostForm("/Extract/" + action, form, "/Extract");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Test period", html);
            Assert.Contains("Knowledge directory", html);
            Assert.Contains(action == "Submit" ? _questionText : "Retain source", html);
        }
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Content == _questionText));
        Assert.IsFalse(await db.KnowledgePoints.AnyAsync(k => k.Title == _lessonTitle));
        Assert.IsFalse(await db.Tags.AnyAsync(t => t.DisplayName == _tagName));
    }

    [TestMethod]
    public async Task DeletedPeriodIsRejectedBySaveServiceBeforeCreatingAnyContent()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.QuestionCategories.Remove((await db.QuestionCategories.FindAsync(_period))!);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IExtractService>()
            .SaveAsync(Data(), _category, _period));
        Assert.IsFalse(await db.KnowledgePoints.AnyAsync(k => k.Title == _lessonTitle));
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Content == _questionText));
        Assert.IsFalse(await db.Tags.AnyAsync(t => t.DisplayName == _tagName));
    }

    [TestMethod]
    public async Task EditingJsonPreservesBothDestinations()
    {
        var response = await PostForm("/Extract/ResumeEdit", Form(), "/Extract");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"value=\"{_period}\"", html);
        Assert.Contains($"value=\"{_category}\"", html);
        Assert.Contains("Original source", html);
        var preview = await PostForm("/Extract/Preview", Form(), "/Extract");
        html = await preview.Content.ReadAsStringAsync();
        Assert.Contains($"selected=\"selected\" value=\"{_period}\"", html);
        Assert.Contains($"selected=\"selected\" value=\"{_category}\"", html);
    }

    [TestMethod]
    public async Task FreeExtractionRejectsPaidExercisesBeforeQueuingAndBeforeSaving()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        (await db.KnowledgeCategories.FindAsync(_category))!.IsFree = true;
        (await db.QuestionCategories.FindAsync(_period))!.IsFree = false;
        await db.SaveChangesAsync();
        foreach (var action in new[] { "Extract", "Submit" })
        {
            var form = Form();
            form["Material"] = "Retain source";
            var response = await PostForm("/Extract/" + action, form, "/Extract");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Choose a free question category", await response.Content.ReadAsStringAsync());
        }
        // A queued task must also recheck prices changed after its form was submitted.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IExtractService>()
            .SaveAsync(Data(), _category, _period));
        Assert.Contains("Choose a free question category", error.Message);
        Assert.IsFalse(await db.KnowledgePoints.AnyAsync(k => k.Title == _lessonTitle));
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Content == _questionText));
        Assert.IsFalse(await db.Tags.AnyAsync(t => t.DisplayName == _tagName));
        (await db.QuestionCategories.FindAsync(_period))!.IsFree = true;
        await db.SaveChangesAsync();
        AssertRedirect(await PostForm("/Extract/Submit", Form(), "/Extract"), "/Extract");
        await AssertSaved();
    }
}
