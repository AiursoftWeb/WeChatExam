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
    private Guid _firstPeriod;
    private Guid _secondPeriod;
    private Guid _institution;
    private Guid _course;
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
        _firstPeriod = await QuestionPeriodAssignmentTests.AddPeriod(_host.Services);
        _secondPeriod = await QuestionPeriodAssignmentTests.AddPeriod(_host.Services);
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var institution = new Institution { Title = "Import institution" };
            db.Institutions.Add(institution); await db.SaveChangesAsync();
            _institution = institution.Id;
            _course = await db.QuestionCategories.Where(c => c.Id == _firstPeriod).Select(c => c.TotalCategoryId).SingleAsync();
        }
        _http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri($"http://localhost:{port}") };
        var login = await Token("/Account/Login");
        var response = await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> { ["EmailOrUserName"] = "admin@default.com", ["Password"] = "Admin@123456!", ["__RequestVerificationToken"] = login }));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        _token = await Token("/Papers/Import");
    }
    [TestCleanup]
    public async Task Cleanup() { _http.Dispose(); await _host.StopAsync(); _host.Dispose(); TestStartupWithMockWeChat.MockOllamaService = null; }
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
    private PaperImportSaveRequest SaveRequest(Guid task)
    {
        var questions = Preview().Questions;
        questions[0].StandardAnswer = "Manually supplied answer";
        questions[2].StandardAnswer = "True";
        for (var i = 0; i < questions.Count; i++) questions[i].QuestionCategoryId = i == 0 ? _firstPeriod : _secondPeriod;
        return new() { Kind = ImportPaperKind.Real, InstitutionId = _institution, TaskId = task, Title = "Edited title", SourceTagName = "Unique source tag " + Guid.NewGuid(), Questions = questions, ExtraTags = ["extra " + Guid.NewGuid()] };
    }

    [TestMethod]
    public async Task ExtractPreviewEditedSaveIsOrderedAndIdempotent()
    {
        using var baselineScope = _host.Services.CreateScope(); var baselineDb = baselineScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var initialPapers = await baselineDb.RealPapers.CountAsync(); var initialQuestions = await baselineDb.Questions.CountAsync();
        var id = await Extract();
        for (var i = 0; i < 3; i++) await _http.GetAsync("/Papers/ImportStatus?id=" + id);
        Assert.AreEqual(1, _ai.Questions.Count(call => call.Contains("Do not solve") && call.Contains("Missing answers") && call.Contains("exact original mixed")));
        using var scope = _host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(initialPapers, await db.RealPapers.CountAsync()); Assert.AreEqual(initialQuestions, await db.Questions.CountAsync());
        var save = SaveRequest(id); save.SourceTagName += Guid.NewGuid(); save.ExtraTags = ["extra " + Guid.NewGuid()]; save.Questions = [save.Questions[2], save.Questions[0]]; save.Questions[0].Content = "User edit";
        var response = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var paperId = JObject.Parse(await response.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>();
        // A retry with changed type still returns the originally committed paper.
        save.Kind = ImportPaperKind.Mock;
        var repeat = await Post("ImportSave", save); Assert.AreEqual(paperId, JObject.Parse(await repeat.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>());
        var paper = await db.RealPapers.SingleAsync(p => p.Id == paperId); Assert.AreEqual(180, paper.TimeLimit); Assert.AreEqual(_institution, paper.InstitutionId); Assert.AreEqual(PaperStatus.Draft, paper.Status);
        var questions = await db.RealPaperQuestions.Where(p => p.RealPaperId == paperId).Include(p => p.Question).OrderBy(p => p.Order).ToListAsync();
        Assert.AreEqual("User edit", questions[0].Question.Content); Assert.AreEqual("First original question", questions[1].Question.Content); Assert.AreEqual("True", questions[0].Question.StandardAnswer); Assert.AreEqual("Manually supplied answer", questions[1].Question.StandardAnswer); Assert.AreEqual(_secondPeriod, questions[0].Question.QuestionCategoryId);
        Assert.AreEqual(_firstPeriod, questions[1].Question.QuestionCategoryId);
        Assert.AreEqual(0, await db.PaperTags.CountAsync(t => t.PaperId == paperId)); Assert.AreEqual(4, await db.QuestionTags.CountAsync(t => questions.Select(q => q.QuestionId).Contains(t.QuestionId))); Assert.AreEqual(0, await db.PaperCategories.CountAsync(c => c.PaperId == paperId));
        Assert.AreEqual(save.SourceTagName.ToUpperInvariant(), (await db.Tags.SingleAsync(t => t.DisplayName == save.SourceTagName)).NormalizedName);
        Assert.IsFalse(await db.Papers.AnyAsync(p => p.Id == paperId));
        Assert.IsFalse(await db.PaperQuestions.AnyAsync(p => p.PaperId == paperId));
        Assert.AreEqual("/RealPapers/Edit/" + paperId, JObject.Parse(await response.Content.ReadAsStringAsync())["url"]!.Value<string>()); Assert.AreEqual(save.ExtraTags[0].ToUpperInvariant(), (await db.Tags.SingleAsync(t => t.DisplayName == save.ExtraTags[0])).NormalizedName);
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
            QuestionCategoryId = _firstPeriod,
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
        var questions = await db.RealPaperQuestions.Where(p => p.RealPaperId == paperId).Include(p => p.Question).ToListAsync();
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
        var beforeCount = await db.RealPapers.CountAsync();
        var denied = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.BadRequest, denied.StatusCode); Assert.AreEqual(beforeCount, await db.RealPapers.CountAsync());
        var check = await Post("ImportCheckTags", new { sourceTagName = save.SourceTagName, extraTags = save.ExtraTags });
        var checkBody = JObject.Parse(await check.Content.ReadAsStringAsync()); Assert.AreEqual(taxonomy.Id, checkBody["effectiveTaxonomy"]!.Value<int>()); Assert.IsNull(checkBody["categories"]); Assert.IsNull(checkBody["conflicts"]![0]!["isFree"]);
        save.Confirmations = [new() { Id = tag.Id, TaxonomyId = null }];
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Post("ImportSave", save)).StatusCode);
        save.Confirmations[0].TaxonomyId = taxonomy.Id;
        var success = await Post("ImportSave", save); Assert.AreEqual(HttpStatusCode.OK, success.StatusCode, await success.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear(); var actual = await db.Tags.SingleAsync(t => t.Id == tag.Id); Assert.AreEqual(tag.NormalizedName, actual.NormalizedName); Assert.AreEqual(taxonomy.Id, actual.TaxonomyId);
    }

    [TestMethod]
    public async Task MissingAnswersRemainInPreviewButCannotBeSaved()
    {
        var id = await Extract();
        var preview = JObject.Parse(await _http.GetStringAsync("/Papers/ImportStatus?id=" + id));
        Assert.AreEqual("", preview["preview"]!["questions"]![0]!["standardAnswer"]!.Value<string>());
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paperCount = await db.RealPapers.CountAsync();
        var questionCount = await db.Questions.CountAsync();
        var tagCount = await db.Tags.CountAsync();
        var tagName = "Unchanged tag " + Guid.NewGuid();
        var tag = new Tag { DisplayName = tagName, NormalizedName = tagName.ToUpperInvariant(), TaxonomyId = null };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        foreach (var missing in new[] { "", " \t\n" })
        {
            var save = SaveRequest(id);
            save.SourceTagName = tagName;
            save.Confirmations = [new() { Id = tag.Id, TaxonomyId = null }];
            save.Questions[0].StandardAnswer = missing;
            var response = await Post("ImportSave", save);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Complete all standard answers before saving");
        }
        db.ChangeTracker.Clear();
        Assert.AreEqual(paperCount, await db.RealPapers.CountAsync());
        Assert.AreEqual(questionCount, await db.Questions.CountAsync());
        Assert.AreEqual(tagCount + 1, await db.Tags.CountAsync());
        Assert.AreEqual(tag.NormalizedName, (await db.Tags.SingleAsync(t => t.Id == tag.Id)).NormalizedName);
        Assert.AreEqual(1, _ai.Questions.Count);
    }

    [TestMethod]
    public async Task InvalidPeriodsLeavePaperQuestionsAndSharedTagsUnchangedThenAllowCorrection()
    {
        var id = await Extract();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var legacy = new Category { Id = Guid.NewGuid(), Title = "Paper classification only", ParentId = null };
        var tagName = "Period validation " + Guid.NewGuid();
        var tag = new Tag { DisplayName = tagName, NormalizedName = tagName.ToUpperInvariant()};
        db.Categories.Add(legacy); db.Tags.Add(tag);
        await db.SaveChangesAsync();
        var deleted = await QuestionPeriodAssignmentTests.AddPeriod(_host.Services);
        db.QuestionCategories.Remove(await db.QuestionCategories.SingleAsync(c => c.Id == deleted));
        await db.SaveChangesAsync();
        var before = new[] { await db.RealPapers.CountAsync(), await db.Questions.CountAsync(), await db.Tags.CountAsync(), await db.QuestionTags.CountAsync(), await db.RealPaperQuestions.CountAsync() };
        var save = SaveRequest(id);
        save.SourceTagName = tagName;
        save.Confirmations = [new() { Id = tag.Id }];
        save.ExtraTags = [];
        foreach (var invalid in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), deleted, legacy.Id })
        {
            save.Questions[1].QuestionCategoryId = invalid;
            var response = await Post("ImportSave", save);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Choose an existing period for question 2", await response.Content.ReadAsStringAsync());
        }
        db.ChangeTracker.Clear();
        CollectionAssert.AreEqual(before, new[] { await db.RealPapers.CountAsync(), await db.Questions.CountAsync(), await db.Tags.CountAsync(), await db.QuestionTags.CountAsync(), await db.RealPaperQuestions.CountAsync() });
        Assert.AreEqual(tag.NormalizedName, (await db.Tags.SingleAsync(t => t.Id == tag.Id)).NormalizedName);
        // Correct the same preview without extracting again. Institution ownership
        // permits these questions from different courses.
        save.Questions[1].QuestionCategoryId = _secondPeriod;
        var saved = await Post("ImportSave", save);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        var paperId = JObject.Parse(await saved.Content.ReadAsStringAsync())["paperId"]!.ToObject<Guid>();
        Assert.IsFalse(await db.PaperCategories.AnyAsync(p => p.PaperId == paperId));
        var questions = await db.RealPaperQuestions.Where(p => p.RealPaperId == paperId).Include(p => p.Question).OrderBy(p => p.Order).ToListAsync();
        CollectionAssert.AreEqual(new Guid?[] { _firstPeriod, _secondPeriod, _secondPeriod }, questions.Select(p => p.Question.QuestionCategoryId).ToArray());
        Assert.AreEqual(1, _ai.Questions.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MockImportUsesCourseAndPreservesSharedTagState(bool anonymous)
    {
        var task = await Extract();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var tag = new Tag { DisplayName = "Shared source " + Guid.NewGuid(), NormalizedName = Guid.NewGuid().ToString() };
        tag.NormalizedName = tag.DisplayName.ToUpperInvariant();
        db.Tags.Add(tag); await db.SaveChangesAsync();
        var oldPapers = await db.Papers.CountAsync();
        var oldLinks = await db.PaperQuestions.CountAsync();
        var oldTags = await db.PaperTags.CountAsync();
        var oldCategories = await db.PaperCategories.CountAsync();
        var save = SaveRequest(task);
        save.Kind = ImportPaperKind.Mock; save.InstitutionId = null; save.TotalCategoryId = _course;
        save.AllowViewAnonymousely = anonymous; save.TimeLimit = 120;
        save.SourceTagName = tag.DisplayName; save.ExtraTags = [];
        save.Confirmations = [new() { Id = tag.Id }];
        foreach (var question in save.Questions) question.QuestionCategoryId = _firstPeriod;
        var response = await Post("ImportSave", save);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        var id = body["paperId"]!.ToObject<Guid>();
        Assert.AreEqual("/MockPapers/Edit/" + id, body["url"]!.Value<string>());
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync(body["url"]!.Value<string>())).StatusCode);
        db.ChangeTracker.Clear();
        var paper = await db.MockPapers.SingleAsync(p => p.Id == id);
        Assert.AreEqual(_course, paper.TotalCategoryId); Assert.AreEqual(anonymous, paper.AllowViewAnonymousely);
        Assert.AreEqual(PaperStatus.Draft, paper.Status); Assert.AreEqual(120, paper.TimeLimit);
        var links = await db.MockPaperQuestions.Where(p => p.MockPaperId == id).Include(p => p.Question).OrderBy(p => p.Order).ToListAsync();
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, links.Select(q => q.Order).ToArray());
        CollectionAssert.AreEqual(new[] { 10, 7, 10 }, links.Select(q => q.Score).ToArray());
        Assert.IsTrue(links.All(q => q.Question.QuestionCategoryId == _firstPeriod));
        Assert.AreEqual(tag.NormalizedName, (await db.Tags.SingleAsync(t => t.Id == tag.Id)).NormalizedName);
        Assert.AreEqual(oldPapers, await db.Papers.CountAsync()); Assert.AreEqual(oldLinks, await db.PaperQuestions.CountAsync());
        Assert.AreEqual(oldTags, await db.PaperTags.CountAsync()); Assert.AreEqual(oldCategories, await db.PaperCategories.CountAsync());
        Assert.IsFalse(await db.RealPapers.AnyAsync(p => p.Id == id));
        Assert.IsFalse(await db.PaperSnapshots.AnyAsync(p => p.MockPaperId == id));
    }

    [TestMethod]
    public async Task InvalidOwnershipAndCrossCourseMockAreAtomicAndCanBeCorrected()
    {
        var task = await Extract();
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var deleted = new Institution { Title = "Deleted institution" };
        db.Institutions.Add(deleted); await db.SaveChangesAsync();
        db.Institutions.Remove(deleted); await db.SaveChangesAsync();
        async Task<int[]> Counts() => [await db.Papers.CountAsync(), await db.MockPapers.CountAsync(), await db.RealPapers.CountAsync(),
            await db.Questions.CountAsync(), await db.Tags.CountAsync(), await db.QuestionTags.CountAsync(),
            await db.MockPaperQuestions.CountAsync(), await db.RealPaperQuestions.CountAsync()];
        var before = await Counts();
        var invalids = new Action<PaperImportSaveRequest>[]
        {
            r => r.Kind = null, r => r.Kind = (ImportPaperKind)99,
            r => r.InstitutionId = null, r => r.InstitutionId = Guid.Empty, r => r.InstitutionId = deleted.Id,
            r => r.TotalCategoryId = _course, r => r.AllowViewAnonymousely = true,
            r => { r.Kind = ImportPaperKind.Mock; r.TotalCategoryId = _course; }, // Schools cannot own mock papers.
            r => { r.Kind = ImportPaperKind.Mock; r.InstitutionId = null; },
            r => { r.Kind = ImportPaperKind.Mock; r.InstitutionId = null; r.TotalCategoryId = Guid.NewGuid(); },
            r => { r.Kind = ImportPaperKind.Mock; r.InstitutionId = null; r.TotalCategoryId = _course; } // Mixed courses.
        };
        foreach (var change in invalids)
        {
            var save = SaveRequest(task); change(save);
            var result = await Post("ImportSave", save);
            Assert.AreEqual(HttpStatusCode.BadRequest, result.StatusCode, await result.Content.ReadAsStringAsync());
            CollectionAssert.AreEqual(before, await Counts());
        }
        // The same reviewed cross-course questions are valid as an institution paper.
        var success = await Post("ImportSave", SaveRequest(task));
        Assert.AreEqual(HttpStatusCode.OK, success.StatusCode, await success.Content.ReadAsStringAsync());
        var body = JObject.Parse(await success.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync(body["url"]!.Value<string>())).StatusCode);
        Assert.AreEqual(1, _ai.Questions.Count);
    }

    [TestMethod]
    public async Task ImportOnlyAccountCanSaveWithoutReceivingAnUnauthorizedEditLink()
    {
        using var scope = _host.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var email = $"import-{Guid.NewGuid():N}@example.com";
        var user = new User { UserName = email, Email = email, DisplayName = "Import reviewer" };
        Assert.IsTrue((await manager.CreateAsync(user, "Import@123456!")).Succeeded);
        foreach (var permission in new[] { Authorization.AppPermissionNames.CanAddPapers, Authorization.AppPermissionNames.CanAddQuestions })
            Assert.IsTrue((await manager.AddClaimAsync(user, new System.Security.Claims.Claim(Authorization.AppPermissions.Type, permission))).Succeeded);
        var login = await Token("/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, (await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["EmailOrUserName"] = email, ["Password"] = "Import@123456!", ["__RequestVerificationToken"] = login
        }))).StatusCode);
        _token = await Token("/Papers/Import");
        var task = await Extract();
        var response = await Post("ImportSave", SaveRequest(task));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(JTokenType.Null, body["url"]!.Type);
        var id = body["paperId"]!.ToObject<Guid>();
        Assert.IsTrue(await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().RealPapers.AnyAsync(p => p.Id == id));
        Assert.AreEqual(HttpStatusCode.Found, (await _http.GetAsync("/RealPapers/Edit/" + id)).StatusCode);
    }

    [TestMethod]
    public async Task ImportPageUsesIndependentOwnershipAndNoLegacyFeeControls()
    {
        foreach (var kind in new[] { "Mock", "Real" })
        {
            var html = await _http.GetStringAsync($"/Papers/Import?kind={kind}&totalCategoryId={_course}&institutionId={_institution}");
            Assert.Contains("name=\"TotalCategoryId\"", html); Assert.Contains("name=\"InstitutionId\"", html);
            Assert.Contains("name=\"AllowViewAnonymousely\"", html);
            Assert.DoesNotContain("name=\"CategoryId\"", html); Assert.DoesNotContain("name=\"IsFree\"", html);
            Assert.DoesNotContain("Source tags are always paid", html);
            Assert.Contains($"value=\"{(kind == "Mock" ? 0 : 1)}\" selected=\"selected\"", html);
            var list = await _http.GetStringAsync("/" + kind + "Papers");
            Assert.Contains("/Papers/Import?kind=" + kind, list);
        }
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
