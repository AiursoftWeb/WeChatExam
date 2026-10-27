using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Controllers.Management;
using Aiursoft.WeChatExam.Models.QuestionImportViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionImportTests : TestBase
{
    private readonly string _batchId = Guid.NewGuid().ToString();
    private string Source => "Import source " + _batchId;
    private string Batch(string? secondAnswer = "B") => JsonConvert.SerializeObject(new[]
    {
        new { Question = "First import question " + _batchId, Options = new[] { "A", "B" }, Answer = (string?)"A", Analysis = "First explanation", OriginalFilename = Source },
        new { Question = "Second import question " + _batchId, Options = new[] { "A", "B" }, Answer = secondAnswer, Analysis = "Second explanation", OriginalFilename = Source }
    });

    private Task<HttpResponseMessage> Submit(string json, params string[] periods)
    {
        var form = new Dictionary<string, string> { ["JsonContent"] = json, ["SelectedQuestionType"] = "0" };
        for (var i = 0; i < periods.Length; i++) form[$"QuestionCategoryIds[{i}]"] = periods[i];
        return PostForm("/QuestionImport/Submit", form, "/QuestionImport");
    }

    [TestMethod]
    public async Task PreviewDefaultsAndPerQuestionPeriodsPreserveQuestionDataAndPaidSourceTag()
    {
        await LoginAsAdmin();
        var first = await QuestionPeriodAssignmentTests.AddPeriod(Server!.Services);
        var second = await QuestionPeriodAssignmentTests.AddPeriod(Server.Services);
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.Tags.Add(new Tag { DisplayName = Source, NormalizedName = Source.ToUpperInvariant(), IsFree = false });
        await db.SaveChangesAsync();
        var preview = await PostForm("/QuestionImport/Preview", new Dictionary<string, string>
        {
            ["JsonContent"] = Batch(), ["SelectedQuestionType"] = "0", ["DefaultQuestionCategoryId"] = first.ToString()
        }, "/QuestionImport");
        var html = await preview.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("name=\"QuestionCategoryIds[0]\"", html);
        Assert.Contains("name=\"QuestionCategoryIds[1]\"", html);
        Assert.AreEqual(2, Regex.Matches(html, "selected=\"selected\"").Count);
        AssertRedirect(await Submit(Batch(), first.ToString(), second.ToString()), "/QuestionImport");
        var questions = await db.Questions.Where(q => q.Content.Contains(_batchId)).Include(q => q.QuestionTags).ThenInclude(q => q.Tag).OrderBy(q => q.Content).ToListAsync();
        Assert.AreEqual(2, questions.Count);
        Assert.AreEqual(first, questions[0].QuestionCategoryId);
        Assert.AreEqual(second, questions[1].QuestionCategoryId);
        Assert.AreEqual("A", questions[0].StandardAnswer);
        Assert.AreEqual("Second explanation", questions[1].Explanation);
        CollectionAssert.AreEqual(new[] { "A", "B" }, JObject.Parse(questions[0].Metadata)["options"]!.ToObject<string[]>()!);
        Assert.IsTrue(questions.All(q => q.QuestionType == QuestionType.Choice && q.GradingStrategy == GradingStrategy.ExactMatch));
        Assert.IsTrue(questions.All(q => q.QuestionTags.Single().Tag.DisplayName == Source && !q.QuestionTags.Single().Tag.IsFree));
    }

    [TestMethod]
    public async Task MissingForgedOrDeletedPeriodsRejectTheWholeBatchBeforeCreatingTags()
    {
        await LoginAsAdmin();
        var first = await QuestionPeriodAssignmentTests.AddPeriod(Server!.Services);
        var deleted = await QuestionPeriodAssignmentTests.AddPeriod(Server.Services);
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.QuestionCategories.Remove(await db.QuestionCategories.SingleAsync(c => c.Id == deleted));
        await db.SaveChangesAsync();
        foreach (var invalid in new[] { "", "not-a-guid", Guid.Empty.ToString(), deleted.ToString(), Guid.NewGuid().ToString() })
        {
            var response = await Submit(Batch(), first.ToString(), invalid);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Nothing has been imported.", html);
            Assert.Contains("First import question", html);
            Assert.Contains("name=\"QuestionCategoryIds[1]\"", html);
        }
        foreach (var periods in new[] { Array.Empty<string>(), new[] { first.ToString() }, new[] { first.ToString(), first.ToString(), first.ToString() } })
            Assert.AreEqual(HttpStatusCode.OK, (await Submit(Batch(), periods)).StatusCode);
        Assert.AreEqual(0, await db.Questions.CountAsync(q => q.Content.Contains(_batchId)));
        Assert.AreEqual(0, await db.Tags.CountAsync(t => t.DisplayName == Source));
        Assert.AreEqual(0, await db.QuestionTags.CountAsync(t => t.Tag.DisplayName == Source));
    }

    [TestMethod]
    public async Task FailedLaterQuestionRollsBackEarlierQuestionAndSourceTag()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var total = new TotalCategory { Title = "Music" };
        var period = new QuestionCategory { Title = "Early music", TotalCategory = total, TotalCategoryId = total.Id };
        db.QuestionCategories.Add(period);
        await db.SaveChangesAsync();
        using var scope = Server!.Services.CreateScope();
        var controller = new QuestionImportController(db, new TagService(db), scope.ServiceProvider.GetRequiredService<IStringLocalizer<QuestionImportController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider } }
        };
        // Unlike the default in-memory provider, SQLite really rolls back the source tag
        // service's early save when the later question violates a required database field.
        var result = await controller.Submit(new QuestionImportPreviewViewModel
        {
            JsonContent = Batch(null), SelectedQuestionType = QuestionType.Choice,
            QuestionCategoryIds = [period.Id, period.Id]
        });
        Assert.IsInstanceOfType<ViewResult>(result);
        var model = (QuestionImportPreviewViewModel)((ViewResult)result).Model!;
        Assert.Contains("Unable to import:", model.ErrorMessage!);
        Assert.AreEqual(0, await db.Questions.CountAsync());
        Assert.AreEqual(0, await db.Tags.CountAsync());
        Assert.AreEqual(0, await db.QuestionTags.CountAsync());
    }

    [TestMethod]
    public async Task ReturningToJsonKeepsRowSelectionsAndExtraFieldsWhenQuestionsAreReordered()
    {
        await LoginAsAdmin();
        var first = await QuestionPeriodAssignmentTests.AddPeriod(Server!.Services);
        var second = await QuestionPeriodAssignmentTests.AddPeriod(Server.Services);
        var data = JArray.Parse(Batch());
        data[0]["extraSourceNote"] = "Keep this field";
        data[0]["questionCategoryId"] = Guid.NewGuid().ToString();
        var response = await PostForm("/QuestionImport/ResumeEdit", new Dictionary<string, string>
        {
            ["JsonContent"] = data.ToString(), ["SelectedQuestionType"] = "0",
            ["QuestionCategoryIds[0]"] = first.ToString(), ["QuestionCategoryIds[1]"] = second.ToString()
        }, "/QuestionImport");
        var html = await response.Content.ReadAsStringAsync();
        var json = WebUtility.HtmlDecode(Regex.Match(html, "<textarea[^>]*name=\"JsonContent\"[^>]*>([\\s\\S]*?)</textarea>").Groups[1].Value);
        var edited = JArray.Parse(json);
        Assert.AreEqual("Keep this field", edited[0]["extraSourceNote"]!.Value<string>());
        Assert.AreEqual(first, edited[0]["QuestionCategoryId"]!.ToObject<Guid>());
        Assert.IsNull(edited[0]["questionCategoryId"]);
        var reordered = new JArray(edited[1].DeepClone(), edited[0].DeepClone());
        var preview = await PostForm("/QuestionImport/Preview", new Dictionary<string, string>
        {
            ["JsonContent"] = reordered.ToString(), ["SelectedQuestionType"] = "0"
        }, "/QuestionImport");
        Assert.AreEqual(HttpStatusCode.OK, preview.StatusCode);
        AssertRedirect(await Submit(reordered.ToString(), second.ToString(), first.ToString()), "/QuestionImport");
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(first, (await db.Questions.SingleAsync(q => q.Content == "First import question " + _batchId)).QuestionCategoryId);
    }

    [TestMethod]
    public async Task DirectSubmitRevalidatesJsonTypeAndChoiceOptions()
    {
        await LoginAsAdmin();
        var period = await QuestionPeriodAssignmentTests.AddPeriod(Server!.Services);
        foreach (var json in new[] { "{", "[]", "[null]", "[{\"Question\":\"No options\"}]" })
            Assert.AreEqual(HttpStatusCode.OK, (await Submit(json, period.ToString())).StatusCode);
        var response = await PostForm("/QuestionImport/Submit", new Dictionary<string, string>
        {
            ["JsonContent"] = Batch(), ["SelectedQuestionType"] = "999",
            ["QuestionCategoryIds[0]"] = period.ToString(), ["QuestionCategoryIds[1]"] = period.ToString()
        }, "/QuestionImport");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var scope = Server.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Questions.CountAsync(q => q.Content.Contains(_batchId)));
    }
}
