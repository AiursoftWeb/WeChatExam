using System.Net;
using System.Text.Json;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ContentSearchTests : TestBase
{
    [TestMethod]
    public async Task SearchRequiresPermissionAndEncodesQuestionContent()
    {
        var anonymous = await Http.GetAsync("/ContentSearch?query=Boya");
        Assert.AreEqual(HttpStatusCode.Found, anonymous.StatusCode);
        await RegisterAndLoginAsync();
        var denied = await Http.GetAsync("/ContentSearch?query=Boya");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden ||
                      denied.StatusCode == HttpStatusCode.Found && denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"),
            $"Unexpected permission response: {denied.StatusCode}, {denied.Headers.Location}");
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.Questions.Add(new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Boya <script>alert('question')</script>" });
        await db.SaveChangesAsync();
        var response = await Http.GetAsync("/ContentSearch?query=Boya");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>alert('question')</script>", html);
        Assert.Contains("returnUrl=", html);
        var editorSearch = await Http.GetAsync("/ContentSearch/Questions?query=Boya&useAi=false&includeSearchStatus=true");
        editorSearch.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await editorSearch.Content.ReadAsStringAsync());
        Assert.IsFalse(json.RootElement.GetProperty("usedAi").GetBoolean());
        var legacySearch = await Http.GetAsync("/ContentSearch/Questions?query=Boya&useAi=false");
        legacySearch.EnsureSuccessStatusCode();
        using var legacyJson = JsonDocument.Parse(await legacySearch.Content.ReadAsStringAsync());
        Assert.AreEqual(JsonValueKind.Array, legacyJson.RootElement.ValueKind,
            "Existing editor tabs must still receive the original array response.");
        Assert.HasCount(1, json.RootElement.GetProperty("results").EnumerateArray().ToArray());
        Assert.AreEqual("Boya <script>alert('question')</script>",
            json.RootElement.GetProperty("results")[0].GetProperty("title").GetString());
    }

    [TestMethod]
    public async Task SearchFiltersSeparatePeriodsFromKnowledgeDirectories()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var directory = new Category { Id = Guid.NewGuid(), Title = "Knowledge directory", ParentId = null };
        var question = new Question { Id = Guid.NewGuid(), QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Content = "scope-fixture" };
        var excluded = new Question { Id = Guid.NewGuid(), QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Content = "scope-fixture excluded", Category = directory };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "scope-fixture point", Content = "Knowledge" };
        db.AddRange(directory, question, excluded, point);
        db.Add(new CategoryKnowledgePoint { CategoryId = directory.Id, KnowledgePointId = point.Id });
        await db.SaveChangesAsync();
        var filters = $"query=scope-fixture&knowledgeCategoryId={directory.Id}&questionCategoryId={question.QuestionCategoryId}";
        var response = await Http.GetAsync("/ContentSearch?" + filters);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"data-content-id=\"{question.Id}\"", html);
        Assert.Contains($"data-content-id=\"{point.Id}\"", html);
        Assert.DoesNotContain($"data-content-id=\"{excluded.Id}\"", html);
        Assert.Contains("name=\"questionCategoryId\"", html);
        Assert.Contains("name=\"knowledgeCategoryId\"", html);
        Assert.DoesNotContain("name=\"categoryId\"", html);
        Assert.Contains("<optgroup", html);
        Assert.Contains("returnUrl=", html);
        var jsonResponse = await Http.GetAsync("/ContentSearch/Questions?" + filters + "&useAi=false");
        jsonResponse.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await jsonResponse.Content.ReadAsStringAsync());
        Assert.HasCount(1, json.RootElement.EnumerateArray().ToArray());
        Assert.AreEqual(question.Id, json.RootElement[0].GetProperty("id").GetGuid());
        foreach (var url in new[] { "/ContentSearch?questionCategoryId=", "/ContentSearch?knowledgeCategoryId=",
                     "/ContentSearch/Questions?questionCategoryId=" })
        {
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync(url + "invalid")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync(url + Guid.NewGuid())).StatusCode);
        }
    }

    [TestMethod]
    public async Task RecommendationsRequireCsrfAndAssociationsAreOnlySavedByTheEditForm()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya", Content = "Guqin" };
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Who played Flowing Water?", CategoryId = category.Id };
        db.AddRange(category, point, question); await db.SaveChangesAsync();
        var vector = new float[1024]; vector[0] = 1;
        point.Embedding = EmbeddingHelper.Serialize(vector); point.LastEmbeddedAt = point.LastUpdatedAt;
        question.Embedding = EmbeddingHelper.Serialize(vector); question.LastEmbeddedAt = question.LastUpdatedAt;
        await db.SaveChangesAsync();
        GetService<IConfiguration>()["OpenAI:CompletionApiUrl"] = "http://127.0.0.1:1/api/chat";
        GetService<IConfiguration>()["GlobalSettings:" + SettingsMap.EnableEmbeddingBasedSearch] = "True";
        var payload = new { id = point.Id, categoryIds = new[] { category.Id } };
        var rejected = await Http.PostAsJsonAsync("/ContentSearch/RecommendedQuestions", payload);
        Assert.AreEqual(HttpStatusCode.BadRequest, rejected.StatusCode);
        var token = await GetAntiCsrfToken($"/KnowledgePoints/Edit/{point.Id}");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/RecommendedQuestions");
        request.Content = JsonContent.Create(payload);
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request); response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(json.RootElement.GetProperty("available").GetBoolean());
        Assert.HasCount(1, json.RootElement.GetProperty("results").EnumerateArray().ToArray());
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync());

        var searchReturn = "/ContentSearch?query=Boya&kind=knowledge";
        var edit = await PostForm($"/KnowledgePoints/Edit/{point.Id}", new Dictionary<string, string>
        {
            ["Id"] = point.Id.ToString(), ["Title"] = point.Title, ["Content"] = point.Content,
            ["SelectedQuestionIds"] = question.Id.ToString(), ["ReturnUrl"] = searchReturn
        }, tokenUrl: $"/KnowledgePoints/Edit/{point.Id}");
        AssertRedirect(edit, searchReturn);
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, await db.KnowledgePointQuestions.CountAsync());
        Assert.IsNotNull((await db.KnowledgePoints.SingleAsync(k => k.Id == point.Id)).Embedding,
            "Saving only associations must preserve the content vector.");
    }

    [TestMethod]
    public async Task PaperQuestionSearchPreservesPermissionsLegacyMtqlAndFrozenPaperRules()
    {
        var id = Guid.NewGuid();
        var url = $"/Papers/SearchQuestions/{id}?mode=keywords&query=paper-finder";
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync(url)).StatusCode);
        await RegisterAndLoginAsync();
        var denied = await Http.GetAsync(url);
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden ||
            denied.StatusCode == HttpStatusCode.Found && denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paper = new Paper { Id = id, Title = "Search fixture" };
        var existing = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "paper-finder existing" };
        var available = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "paper-finder " + new string('x', 120) };
        var tag = new Tag { DisplayName = "finder", NormalizedName = "FINDER" };
        db.AddRange(paper, existing, available, tag); await db.SaveChangesAsync();
        db.AddRange(new PaperQuestion { PaperId = id, QuestionId = existing.Id, Order = 1, Score = 10 },
            new QuestionTag { QuestionId = existing.Id, TagId = tag.Id },
            new QuestionTag { QuestionId = available.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        var response = await Http.GetAsync(url); response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsFalse(json.RootElement.GetProperty("usedAi").GetBoolean());
        Assert.HasCount(1, json.RootElement.GetProperty("results").EnumerateArray().ToArray());
        Assert.AreEqual(available.Id, json.RootElement.GetProperty("results")[0].GetProperty("id").GetGuid());
        var legacy = await Http.GetAsync($"/Papers/SearchQuestions/{id}?mtql=finder"); legacy.EnsureSuccessStatusCode();
        using var legacyJson = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.AreEqual(JsonValueKind.Array, legacyJson.RootElement.ValueKind);
        Assert.HasCount(1, legacyJson.RootElement.EnumerateArray().ToArray());
        Assert.AreEqual(103, legacyJson.RootElement[0].GetProperty("content").GetString()!.Length);
        Assert.AreEqual(available.Content, legacyJson.RootElement[0].GetProperty("preview").GetString());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/Papers/SearchQuestions/{id}?mode=invalid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/Papers/SearchQuestions/{Guid.NewGuid()}?mode=keywords&query=paper-finder")).StatusCode);

        var token = await GetAntiCsrfToken($"/Papers/Edit/{id}");
        paper.Status = PaperStatus.Frozen; await db.SaveChangesAsync();
        var edit = await Http.GetAsync($"/Papers/Edit/{id}");
        AssertRedirect(edit, $"/Papers/Details/{id}");
        await PostForm($"/Papers/BatchAddQuestions/{id}", new Dictionary<string, string>
        {
            ["questionIds"] = available.Id.ToString(), ["startingOrder"] = "2", ["defaultScore"] = "20",
            ["__RequestVerificationToken"] = token
        });
        Assert.AreEqual(1, await db.PaperQuestions.CountAsync(q => q.PaperId == id));
    }

    [TestMethod]
    public async Task PaperSimilarityRequiresReadPermissionExcludesExistingQuestionsAndNeverReplacesThem()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanEditPapers))).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paper = new Paper { Id = Guid.NewGuid(), Title = "Similar paper fixture" };
        var category = new Category { Id = Guid.NewGuid(), Title = "Similar category", ParentId = null };
        var source = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Original <script>fixture</script>", CategoryId = category.Id };
        var other = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Already in paper", CategoryId = category.Id };
        var candidates = Enumerable.Range(0, 7).Select(i => new Question
        {
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
            Id = Guid.NewGuid(), Content = "Similar candidate " + i, CategoryId = category.Id,
            StandardAnswer = "Answer " + i, Explanation = "Explanation " + i
        }).ToArray();
        db.AddRange(paper, category, source, other); db.Questions.AddRange(candidates); await db.SaveChangesAsync();
        db.AddRange(new PaperQuestion { PaperId = paper.Id, QuestionId = source.Id, Order = 3, Score = 7 },
            new PaperQuestion { PaperId = paper.Id, QuestionId = other.Id, Order = 5, Score = 11 });
        var vector = new float[1024]; vector[0] = 1;
        foreach (var question in candidates.Append(source).Append(other))
        {
            question.Embedding = EmbeddingHelper.Serialize(vector); question.LastEmbeddedAt = question.LastUpdatedAt;
        }
        await db.SaveChangesAsync();
        GetService<IConfiguration>()["GlobalSettings:" + SettingsMap.EnableEmbeddingBasedSearch] = "True";
        GetService<IConfiguration>()["GlobalSettings:" + SettingsMap.EmbeddingOllamaInstance] = "http://127.0.0.1:1";
        var url = $"/Papers/SimilarQuestions/{paper.Id}";
        async Task<HttpResponseMessage> Query(object body, bool csrf = true, string? endpoint = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint ?? url);
            request.Content = JsonContent.Create(body);
            if (csrf) request.Headers.Add("RequestVerificationToken", await GetAntiCsrfToken($"/Papers/Edit/{paper.Id}"));
            return await Http.SendAsync(request);
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var page = await Http.GetStringAsync($"/Papers/Edit/{paper.Id}");
        Assert.DoesNotContain("data-similar-question=", page);
        var denied = await Query(new { id = source.Id });
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanReadQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        page = await Http.GetStringAsync($"/Papers/Edit/{paper.Id}");
        Assert.Contains($"data-similar-question=\"{source.Id}\"", page);
        Assert.Contains("&lt;script&gt;fixture&lt;/script&gt;", page);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Query(new { id = source.Id }, false)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Query(new { id = source.Id, text = "Different draft" })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Query(new { id = Guid.Empty })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Query(new { id = source.Id, categoryIds = new Guid[1001] })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Query(new { id = candidates[0].Id })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Query(new { id = source.Id }, endpoint: $"/Papers/SimilarQuestions/{Guid.NewGuid()}")).StatusCode);
        var response = await Query(new { id = source.Id, categoryIds = new[] { category.Id }, excludeIds = new[] { candidates[0].Id } });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsTrue(json.RootElement.GetProperty("available").GetBoolean());
        var results = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.HasCount(5, results);
        Assert.IsTrue(results.All(q => q.GetProperty("id").GetGuid() != source.Id && q.GetProperty("id").GetGuid() != other.Id && q.GetProperty("id").GetGuid() != candidates[0].Id));
        Assert.IsTrue(results.All(q => q.GetProperty("answer").GetString()!.StartsWith("Answer ") && q.GetProperty("explanation").GetString()!.StartsWith("Explanation ")));
        var originalLinks = await db.PaperQuestions.Where(q => q.PaperId == paper.Id).OrderBy(q => q.Order).ToListAsync();
        Assert.AreEqual(2, originalLinks.Count); Assert.AreEqual(3, originalLinks[0].Order); Assert.AreEqual(7, originalLinks[0].Score);
        Assert.AreEqual(9, await db.Questions.CountAsync(q => candidates.Select(c => c.Id).Append(source.Id).Append(other.Id).Contains(q.Id)));
        await PostForm($"/Papers/BatchAddQuestions/{paper.Id}", new Dictionary<string, string>
        {
            ["questionIds"] = results[0].GetProperty("id").GetString()!, ["startingOrder"] = "7", ["defaultScore"] = "13"
        }, tokenUrl: $"/Papers/Edit/{paper.Id}");
        db.ChangeTracker.Clear();
        var links = await db.PaperQuestions.Where(q => q.PaperId == paper.Id).OrderBy(q => q.Order).ToListAsync();
        Assert.AreEqual(3, links.Count); Assert.AreEqual(source.Id, links[0].QuestionId); Assert.AreEqual(7, links[0].Score);
        Assert.AreEqual(7, links[2].Order); Assert.AreEqual(13, links[2].Score);
        var frozen = await db.Papers.SingleAsync(p => p.Id == paper.Id); frozen.Status = PaperStatus.Frozen; await db.SaveChangesAsync();
        await PostForm($"/Papers/BatchAddQuestions/{paper.Id}", new Dictionary<string, string>
        {
            ["questionIds"] = candidates[0].Id.ToString(), ["startingOrder"] = "8", ["defaultScore"] = "20"
        }, tokenUrl: "/");
        Assert.AreEqual(3, await db.PaperQuestions.CountAsync(q => q.PaperId == paper.Id));
    }

    [TestMethod]
    public async Task KnowledgePointCreationKeepsSelectedQuestionsAfterValidationAndSavesAssociations()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Create finder <script>example</script>" };
        db.Questions.Add(question); await db.SaveChangesAsync();
        var response = await Http.GetAsync("/KnowledgePoints/Create"); response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("value=\"smart\"", html);
        Assert.Contains("value=\"keywords\"", html);
        Assert.Contains("value=\"mtql\"", html);
        Assert.Contains("/js/content-editor.js", html);
        var form = new Dictionary<string, string>
        {
            ["Title"] = "", ["Content"] = "Unsaved knowledge content",
            ["SelectedQuestionIds"] = question.Id.ToString()
        };
        var invalid = await PostForm("/KnowledgePoints/Create", form);
        invalid.EnsureSuccessStatusCode();
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains($"value=\"{question.Id}\"", invalidHtml);
        Assert.Contains("Unsaved knowledge content", invalidHtml);
        Assert.Contains("&lt;script&gt;example&lt;/script&gt;", invalidHtml);
        Assert.Contains("data-remove-question", invalidHtml);
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync(q => q.QuestionId == question.Id));
        form["Title"] = "New knowledge point";
        var saved = await PostForm("/KnowledgePoints/Create", form);
        Assert.AreEqual(HttpStatusCode.Found, saved.StatusCode);
        var link = await db.KnowledgePointQuestions.SingleAsync(q => q.QuestionId == question.Id);
        Assert.AreEqual(question.Id, link.QuestionId);
        var edit = await Http.GetAsync($"/KnowledgePoints/Edit/{link.KnowledgePointId}"); edit.EnsureSuccessStatusCode();
        var editHtml = await edit.Content.ReadAsStringAsync();
        Assert.Contains("value=\"smart\"", editHtml);
        Assert.Contains("data-question-recommendations", editHtml);
    }

    [TestMethod]
    public async Task SimilarKnowledgePointsRequireEditorPermissionCsrfAndExistingSourceWithoutWriting()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanReadQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var source = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Source", Content = "Original content" };
        var similar = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Similar <script>example</script>", Content = "Related content" };
        db.AddRange(source, similar); await db.SaveChangesAsync();
        var vector = new float[1024]; vector[0] = 1;
        foreach (var point in new[] { source, similar }) { point.Embedding = EmbeddingHelper.Serialize(vector); point.LastEmbeddedAt = point.LastUpdatedAt; }
        await db.SaveChangesAsync();
        GetService<IConfiguration>()["GlobalSettings:" + SettingsMap.EnableEmbeddingBasedSearch] = "True";
        GetService<IConfiguration>()["GlobalSettings:" + SettingsMap.EmbeddingOllamaInstance] = "http://127.0.0.1:1";
        var endpoint = "/ContentSearch/SimilarKnowledgePoints";
        var token = await GetAntiCsrfToken("/");
        using var forbidden = new HttpRequestMessage(HttpMethod.Post, endpoint);
        forbidden.Content = JsonContent.Create(new { id = source.Id }); forbidden.Headers.Add("RequestVerificationToken", token);
        var denied = await Http.SendAsync(forbidden);
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        await LoginAsAdmin();
        var editUrl = $"/KnowledgePoints/Edit/{source.Id}";
        var html = await Http.GetStringAsync(editUrl);
        Assert.Contains("data-knowledge-point-recommendations", html);
        Assert.Contains("data-question-recommendations", html);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(endpoint, new { id = source.Id })).StatusCode);
        token = await GetAntiCsrfToken(editUrl);
        foreach (var id in new[] { source.Id, Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { id });
            var response = await Http.SendAsync(request);
            if (id != source.Id) { Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode); continue; }
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.IsTrue(json.RootElement.GetProperty("available").GetBoolean());
            var results = json.RootElement.GetProperty("results").EnumerateArray().ToArray();
            Assert.IsTrue(results.Length is > 0 and <= 5);
            Assert.IsTrue(results.Any(k => k.GetProperty("id").GetGuid() == similar.Id));
            Assert.IsTrue(results.All(k => k.GetProperty("isKnowledgePoint").GetBoolean() && k.GetProperty("id").GetGuid() != source.Id));
        }
        db.ChangeTracker.Clear();
        Assert.AreEqual("Original content", (await db.KnowledgePoints.SingleAsync(k => k.Id == source.Id)).Content);
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync(k => k.KnowledgePointId == source.Id));
    }

    [TestMethod]
    public async Task DraftSimilarityAllowsCreatorsWithoutEditPermissionAndRequiresCsrfAndValidText()
    {
        var endpoint = "/ContentSearch/SimilarQuestionDrafts";
        Assert.AreEqual(HttpStatusCode.Found, (await Http.PostAsJsonAsync(endpoint, new { text = "Draft" })).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanReadQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var token = await GetAntiCsrfToken("/");
        using (var forbidden = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            forbidden.Content = JsonContent.Create(new { text = "Draft" }); forbidden.Headers.Add("RequestVerificationToken", token);
            var denied = await Http.SendAsync(forbidden);
            Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        }
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanAddQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var page = await Http.GetStringAsync("/Questions/Create");
        Assert.Contains("data-draft=\"true\"", page); Assert.Contains("/js/content-editor.js", page);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(endpoint, new { text = "Draft" })).StatusCode);
        token = await GetAntiCsrfToken("/Questions/Create");
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var originalCount = await db.Questions.CountAsync();
        foreach (var text in new[] { "Draft with options and an answer", "", new string('x', 20001) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { text });
            var response = await Http.SendAsync(request);
            Assert.AreEqual(text.Length is > 0 and <= 20000 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { id = Guid.NewGuid(), text = "Draft" });
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.SendAsync(request)).StatusCode);
        }
        Assert.AreEqual(originalCount, await db.Questions.CountAsync());
        await LoginAsAdmin();
        token = await GetAntiCsrfToken("/Questions/Create");
        using var savedRequest = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/SimilarQuestions");
        savedRequest.Headers.Add("RequestVerificationToken", token); savedRequest.Content = JsonContent.Create(new { id = Guid.NewGuid(), text = "Draft" });
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.SendAsync(savedRequest)).StatusCode,
            "The existing saved-question endpoint must still reject missing source records.");
    }

    [TestMethod]
    public async Task ImportComparisonRequiresQuestionReadPermissionWithoutChangingImportAccess()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        foreach (var permission in new[] { Authorization.AppPermissionNames.CanAddPapers, Authorization.AppPermissionNames.CanAddQuestions })
            Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(Authorization.AppPermissions.Type, permission))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var page = await Http.GetStringAsync("/Papers/Import");
        Assert.Contains("id=\"extract\"", page);
        Assert.DoesNotContain("id=\"import-question-comparison\"", page);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanReadQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        page = await Http.GetStringAsync("/Papers/Import");
        Assert.Contains("id=\"import-question-comparison\" hidden", page);
        Assert.Contains("data-endpoint=\"/ContentSearch/SimilarQuestionDrafts\"", page);
        Assert.Contains("data-can-edit=\"false\"", page);
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var originalQuestions = await db.Questions.CountAsync();
        var originalPapers = await db.Papers.CountAsync();
        var originalLinks = await db.PaperQuestions.CountAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/SimilarQuestionDrafts");
        request.Headers.Add("RequestVerificationToken", await GetAntiCsrfToken("/Papers/Import"));
        request.Content = JsonContent.Create(new { text = "Edited import preview question", categoryIds = Array.Empty<Guid>() });
        Assert.AreEqual(HttpStatusCode.OK, (await Http.SendAsync(request)).StatusCode);
        Assert.AreEqual(originalQuestions, await db.Questions.CountAsync());
        Assert.AreEqual(originalPapers, await db.Papers.CountAsync());
        Assert.AreEqual(originalLinks, await db.PaperQuestions.CountAsync());
    }

    [TestMethod]
    public async Task KnowledgeDraftSimilarityRequiresEditorPermissionCsrfAndValidTextWithoutWriting()
    {
        var endpoint = "/ContentSearch/SimilarKnowledgePointDrafts";
        Assert.AreEqual(HttpStatusCode.Found, (await Http.PostAsJsonAsync(endpoint, new { text = "Draft" })).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(
            Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanReadQuestions))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var token = await GetAntiCsrfToken("/");
        using (var forbidden = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            forbidden.Headers.Add("RequestVerificationToken", token); forbidden.Content = JsonContent.Create(new { text = "Draft" });
            var denied = await Http.SendAsync(forbidden);
            Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        }
        await LoginAsAdmin();
        var html = await Http.GetStringAsync("/KnowledgePoints/Create");
        Assert.Contains("data-draft=\"true\"", html);
        Assert.Contains("knowledge-point-recommendations.js", html);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(endpoint, new { text = "Draft" })).StatusCode);
        token = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var pointCount = await db.KnowledgePoints.CountAsync();
        var relationshipCount = await db.KnowledgePointQuestions.CountAsync();
        foreach (var text in new[] { "Draft title and content", " ", new string('x', 20001) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { text });
            var response = await Http.SendAsync(request);
            Assert.AreEqual(!string.IsNullOrWhiteSpace(text) && text.Length <= 20000 ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { id = Guid.NewGuid(), text = "Draft" });
            Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.SendAsync(request)).StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/SimilarKnowledgePoints"))
        {
            request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { id = Guid.NewGuid(), text = "Draft" });
            Assert.AreEqual(HttpStatusCode.NotFound, (await Http.SendAsync(request)).StatusCode);
        }
        Assert.AreEqual(pointCount, await db.KnowledgePoints.CountAsync());
        Assert.AreEqual(relationshipCount, await db.KnowledgePointQuestions.CountAsync());
    }

    [TestMethod]
    public async Task QuestionReorderingPreservesEmbeddingAndReturnsToSearch()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(Server!.Services);
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Boya", QuestionType = QuestionType.Blank, CategoryId = category.Id };
        db.AddRange(category, question); await db.SaveChangesAsync();
        question.Embedding = new byte[4096]; question.LastEmbeddedAt = question.LastUpdatedAt;
        await db.SaveChangesAsync();
        var response = await PostForm($"/Questions/Edit/{question.Id}", new Dictionary<string, string>
        {
            ["Id"] = question.Id.ToString(), ["Content"] = question.Content, ["QuestionType"] = "1",
            ["GradingStrategy"] = "0", ["QuestionCategoryId"] = periodId.ToString(), ["OrderIndex"] = "10",
            ["ReturnUrl"] = "/ContentSearch?query=Boya"
        }, tokenUrl: $"/Questions/Edit/{question.Id}");
        AssertRedirect(response, "/ContentSearch?query=Boya");
        db.ChangeTracker.Clear();
        var saved = await db.Questions.SingleAsync(q => q.Id == question.Id);
        Assert.IsNotNull(saved.Embedding);
        Assert.AreEqual(10m, saved.OrderIndex);
    }
}
