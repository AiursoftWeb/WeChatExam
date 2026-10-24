using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;
using Aiursoft.WeChatExam.Tests.TestServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public class PaperImportTests
{
    private IHost _host = null!;
    private HttpClient _http = null!;
    private StubOllamaService _ai = null!;
    private string _token = null!;
    private static PaperImportPreview Preview() => new()
    {
        Title = "Source title",
        Questions = [new() { OriginalNumber = "1", QuestionType = QuestionType.ShortAnswer, Content = "First original question" }, new() { OriginalNumber = "2", QuestionType = QuestionType.Choice, Content = "Second original question", Options = ["A. one", "B. two"], StandardAnswer = "B. two", Score = 7 }, new() { OriginalNumber = "3", QuestionType = QuestionType.Bool, Content = "Third original question", Options = ["True", "False"] }]
    };
    [TestInitialize]
    public async Task Setup()
    {
        _ai = new StubOllamaService();
        _ai.Response = JsonConvert.SerializeObject(Preview());
        TestStartupWithMockWeChat.MockOllamaService = _ai;
        TestStartupWithMockWeChat.MockWeChatService = new StubWeChatService();
        TestStartupWithMockWeChat.MockWeChatPayService = new StubWeChatPayService();
        using var reservation = new TestPortReservation();
        var port = reservation.Port;
        _host = await AppAsync<TestStartupWithMockWeChat>([], port: port);
        try
        {
            reservation.ConfigureServer(_host);
            await _host.UpdateDbAsync<WeChatExamDbContext>();
            await _host.SeedAsync();
            await _host.StartAsync();
        }
        catch
        {
            _host.Dispose();
            throw;
        }
        _http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://localhost:{port}") };
        var login = await Token("/Account/Login");
        var response = await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["EmailOrUserName"] = "admin@default.com", ["Password"] = "Admin@123456!", ["__RequestVerificationToken"] = login }));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        _token = await Token("/Papers/Import");
    }
    [TestCleanup]
    public async Task Cleanup()
    {
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.LegacyPaperCollectionAliases.RemoveRange(db.LegacyPaperCollectionAliases);
            db.LegacyPaperCollectionBindings.RemoveRange(db.LegacyPaperCollectionBindings);
            db.LegacyPaperCollectionCatalogs.RemoveRange(db.LegacyPaperCollectionCatalogs);
            db.PaperCollectionPapers.RemoveRange(db.PaperCollectionPapers);
            db.PaperCollections.RemoveRange(db.PaperCollections);
            await db.SaveChangesAsync();
        }
        _http.Dispose(); await _host.StopAsync(); _host.Dispose(); TestStartupWithMockWeChat.MockOllamaService = null;
    }
    private async Task<string> Token(string url)
    {
        var response = await _http.GetAsync(url); response.EnsureSuccessStatusCode();
        return Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }
    private async Task<HttpResponseMessage> Post(string action, object body, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Papers/" + action) { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };
        if (csrf) request.Headers.Add("RequestVerificationToken", _token);
        return await _http.SendAsync(request);
    }
    private async Task<Guid> Extract()
    {
        var response = await Post("ImportExtract", new { source = "Original mixed source; no answer for first or third." });
        response.EnsureSuccessStatusCode(); var id = JObject.Parse(await response.Content.ReadAsStringAsync())["taskId"]!.ToObject<Guid>();
        for (var i = 0; i < 100; i++)
        {
            var result = JObject.Parse(await _http.GetStringAsync("/Papers/ImportStatus?id=" + id));
            if (result["status"]!.Value<string>() == "Completed") return id;
            Assert.AreNotEqual("Failed", result["status"]!.Value<string>(), result.ToString());
            await Task.Delay(50);
        }
        Assert.Fail("Extraction did not complete."); return default;
    }
    private static PaperImportSaveRequest SaveRequest(Guid task)
    {
        var questions = Preview().Questions;
        questions[0].StandardAnswer = "Manually supplied answer";
        questions[2].StandardAnswer = "True";
        return new() { TaskId = task, Title = "Edited title", SourceTagName = "Unique source tag", Questions = questions, ExtraTags = ["extra"] };
    }

    [TestMethod]
    public async Task ConnectedDirectorySeparatesQuestionSourceFromPaperCollection()
    {
        Guid collectionId; int taxonomyId, directoryTagId;
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var taxonomy = new Taxonomy { Name = "Colleges" };
            var tag = new Tag { DisplayName = "Suzhou collection", NormalizedName = "SUZHOU COLLECTION", Taxonomy = taxonomy, IsFree = true };
            db.Tags.Add(tag); await db.SaveChangesAsync(); taxonomyId = taxonomy.Id; directoryTagId = tag.Id;
            var service = scope.ServiceProvider.GetRequiredService<PaperCollectionService>();
            await service.ImportAsync(taxonomyId, (await service.PreviewAsync(taxonomyId)).Fingerprint);
            collectionId = await db.PaperCollections.Select(c => c.Id).SingleAsync();
        }
        var task = await Extract();
        var save = SaveRequest(task); save.SourceTagName = "2025 Suzhou source";
        save.TaxonomyId = taxonomyId; save.CollectionIds = [collectionId];
        var response = await Post("ImportSave", save);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var paperId = JObject.Parse(await response.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>();
        using var verify = _host.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(collectionId, await context.PaperCollectionPapers.Where(p => p.PaperId == paperId).Select(p => p.CollectionId).SingleAsync());
        Assert.AreEqual(directoryTagId, await context.PaperTags.Where(p => p.PaperId == paperId).Select(p => p.TagId).SingleAsync());
        var source = await context.Tags.SingleAsync(t => t.DisplayName == save.SourceTagName);
        Assert.AreEqual(taxonomyId, source.TaxonomyId);
        Assert.IsFalse(source.IsFree, "Directory migration must not change the existing importer fee rule.");
        Assert.AreEqual(3, await context.QuestionTags.CountAsync(t => t.TagId == source.Id));
        Assert.AreEqual(0, await context.QuestionTags.CountAsync(t => t.TagId == directoryTagId));
        var legacy = await verify.ServiceProvider.GetRequiredService<PaperCollectionService>().LegacyTagsAsync(taxonomyId);
        CollectionAssert.AreEqual(new[] { directoryTagId }, legacy!.Select(t => t.Id).ToArray());
    }

    [TestMethod]
    public async Task ExtractPreviewEditedSaveIsOrderedAndIdempotent()
    {
        using var baselineScope = _host.Services.CreateScope(); var baselineDb = baselineScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var initialPapers = await baselineDb.Papers.CountAsync(); var initialQuestions = await baselineDb.Questions.CountAsync();
        var id = await Extract();
        for (var i = 0; i < 3; i++) await _http.GetAsync("/Papers/ImportStatus?id=" + id);
        Assert.AreEqual(1, _ai.Questions.Count(call => call.Contains("Do not solve") && call.Contains("Missing answers") && call.Contains("exact original mixed")));
        using var scope = _host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(initialPapers, await db.Papers.CountAsync()); Assert.AreEqual(initialQuestions, await db.Questions.CountAsync());
        var save = SaveRequest(id); save.SourceTagName += Guid.NewGuid(); save.ExtraTags = ["extra " + Guid.NewGuid()]; save.Questions = [save.Questions[2], save.Questions[0]]; save.Questions[0].Content = "User edit";
        var response = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var paperId = JObject.Parse(await response.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>();
        var repeat = await Post("ImportSave", save); Assert.AreEqual(paperId, JObject.Parse(await repeat.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>());
        var paper = await db.Papers.SingleAsync(p => p.Id == paperId); Assert.AreEqual(180, paper.TimeLimit); Assert.IsTrue(paper.IsRealExam); Assert.IsFalse(paper.IsFree); Assert.AreEqual(PaperStatus.Draft, paper.Status);
        var questions = await db.PaperQuestions.Where(p => p.PaperId == paperId).Include(p => p.Question).OrderBy(p => p.Order).ToListAsync();
        Assert.AreEqual("User edit", questions[0].Question.Content); Assert.AreEqual("First original question", questions[1].Question.Content); Assert.AreEqual("True", questions[0].Question.StandardAnswer); Assert.AreEqual("Manually supplied answer", questions[1].Question.StandardAnswer); Assert.IsTrue(questions.All(p => p.Question.CategoryId == null));
        Assert.AreEqual(2, await db.PaperTags.CountAsync(t => t.PaperId == paperId)); Assert.AreEqual(4, await db.QuestionTags.CountAsync(t => questions.Select(q => q.QuestionId).Contains(t.QuestionId))); Assert.AreEqual(0, await db.PaperCategories.CountAsync(c => c.PaperId == paperId));
        Assert.IsFalse((await db.Tags.SingleAsync(t => t.DisplayName == save.SourceTagName)).IsFree); Assert.IsTrue((await db.Tags.SingleAsync(t => t.DisplayName == save.ExtraTags[0])).IsFree);
    }

    [TestMethod]
    public async Task OptionQuestionsUseExactMatchAndOtherTypesUseAiEvaluation()
    {
        var id = await Extract();
        var save = SaveRequest(id);
        save.SourceTagName = "Grading strategy " + Guid.NewGuid();
        save.ExtraTags = [];
        save.Questions = Enum.GetValues<QuestionType>().Select(type => new PaperImportQuestion
        {
            QuestionType = type,
            Content = "Question " + type,
            Options = type is QuestionType.Choice or QuestionType.Bool ? ["Yes", "No"] : [],
            StandardAnswer = type is QuestionType.Choice or QuestionType.Bool ? "Yes" : "Reference answer"
        }).ToList();
        var response = await Post("ImportSave", save);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var paperId = JObject.Parse(await response.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var questions = await db.PaperQuestions.Where(p => p.PaperId == paperId).Include(p => p.Question).ToListAsync();
        Assert.AreEqual(6, questions.Count);
        foreach (var item in questions)
        {
            var expected = item.Question.QuestionType is QuestionType.Choice or QuestionType.Bool
                ? GradingStrategy.ExactMatch : GradingStrategy.AiEval;
            Assert.AreEqual(expected, item.Question.GradingStrategy);
        }
    }

    [TestMethod]
    public async Task TagConflictsRequireCurrentConfirmationAndPreserveTaxonomy()
    {
        var id = await Extract();
        using var scope = _host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var taxonomy = new Taxonomy { Name = "Actual taxonomy" }; db.Taxonomies.Add(taxonomy); await db.SaveChangesAsync();
        var tagName = "Conflict source " + Guid.NewGuid();
        var tag = new Tag { DisplayName = tagName, NormalizedName = tagName.ToUpperInvariant(), TaxonomyId = taxonomy.Id }; db.Tags.Add(tag); await db.SaveChangesAsync();
        var save = SaveRequest(id); save.SourceTagName = tagName; save.ExtraTags = [];
        var beforeCount = await db.Papers.CountAsync();
        var denied = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.BadRequest, denied.StatusCode); Assert.AreEqual(beforeCount, await db.Papers.CountAsync());
        var check = await Post("ImportCheckTags", new { sourceTagName = save.SourceTagName, extraTags = save.ExtraTags });
        var checkBody = JObject.Parse(await check.Content.ReadAsStringAsync()); Assert.AreEqual(taxonomy.Id, checkBody["effectiveTaxonomy"]!.Value<int>()); Assert.IsFalse(checkBody["mismatch"]!.Value<bool>());
        save.Confirmations = [new() { Id = tag.Id, IsFree = false, TaxonomyId = taxonomy.Id }];
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        save.Confirmations[0].IsFree = true;
        var success = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.OK, success.StatusCode, await success.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear(); var actual = await db.Tags.SingleAsync(t => t.Id == tag.Id); Assert.IsFalse(actual.IsFree); Assert.AreEqual(taxonomy.Id, actual.TaxonomyId);
    }

    [TestMethod]
    public async Task MissingAnswersRemainInPreviewButCannotBeSaved()
    {
        var id = await Extract();
        var preview = JObject.Parse(await _http.GetStringAsync("/Papers/ImportStatus?id=" + id));
        Assert.AreEqual("", preview["preview"]!["questions"]![0]!["standardAnswer"]!.Value<string>());
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paperCount = await db.Papers.CountAsync();
        var questionCount = await db.Questions.CountAsync();
        var tagCount = await db.Tags.CountAsync();
        var tagName = "Unchanged free tag " + Guid.NewGuid();
        var tag = new Tag { DisplayName = tagName, NormalizedName = tagName.ToUpperInvariant(), TaxonomyId = null };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        foreach (var missing in new[] { "", " \t\n" })
        {
            var save = SaveRequest(id);
            save.SourceTagName = tagName;
            save.Confirmations = [new() { Id = tag.Id, IsFree = true, TaxonomyId = null }];
            save.Questions[0].StandardAnswer = missing;
            var response = await Post("ImportSave", save);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Complete all standard answers before saving");
        }
        db.ChangeTracker.Clear();
        Assert.AreEqual(paperCount, await db.Papers.CountAsync());
        Assert.AreEqual(questionCount, await db.Questions.CountAsync());
        Assert.AreEqual(tagCount + 1, await db.Tags.CountAsync());
        Assert.IsTrue((await db.Tags.SingleAsync(t => t.Id == tag.Id)).IsFree);
        Assert.AreEqual(1, _ai.Questions.Count);
    }

    [TestMethod]
    public async Task InvalidSourceEnumsOptionsNullRequestsAndCsrfAreRejected()
    {
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportExtract", new { source = new string('x', 100001) })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportExtract", null!)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportCheckTags", null!)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", null!)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportExtract", new { source = "valid" }, false)).StatusCode);
        var id = await Extract(); var save = SaveRequest(id); save.Questions[0].QuestionType = (QuestionType)99;
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        save = SaveRequest(id); save.Questions[1].StandardAnswer = "Not an option";
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        save = SaveRequest(id); save.Questions[0].Options = ["Unexpected option"];
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        save = SaveRequest(id); save.ExtraTags = null!;
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        foreach (var field in new[] { "options", "standardAnswer", "explanation", "originalNumber" })
        {
            var malformed = JObject.FromObject(SaveRequest(id));
            // JObject.FromObject retains C# property casing.
            var property = malformed["Questions"]![0]!.Children<JProperty>()
                .Single(p => string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase));
            property.Value = JValue.CreateNull();
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", malformed)).StatusCode);
        }
        save = SaveRequest(id); save.Confirmations = [null!];
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        var tasks = _host.Services.GetRequiredService<PaperImportTasks>(); Assert.IsNull(tasks.Get(id, "different user"));
    }
}
