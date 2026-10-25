using System.Net;
using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Util;
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
        db.Questions.Add(new Question { Id = Guid.NewGuid(), Content = "Boya <script>alert('question')</script>" });
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
    public async Task RecommendationsRequireCsrfAndAssociationsAreOnlySavedByTheEditForm()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya", Content = "Guqin", ParentId = null };
        var question = new Question { Id = Guid.NewGuid(), Content = "Who played Flowing Water?", CategoryId = category.Id };
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
        var existing = new Question { Id = Guid.NewGuid(), Content = "paper-finder existing" };
        var available = new Question { Id = Guid.NewGuid(), Content = "paper-finder " + new string('x', 120) };
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
    public async Task KnowledgePointCreationKeepsSelectedQuestionsAfterValidationAndSavesAssociations()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var question = new Question { Id = Guid.NewGuid(), Content = "Create finder <script>example</script>" };
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
    public async Task QuestionKnowledgePointSelectionSurvivesValidationAndOnlyExplicitSaveChangesLinks()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var question = new Question { Id = Guid.NewGuid(), Content = "Reverse association", QuestionType = QuestionType.Blank, CategoryId = category.Id };
        var old = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Existing point", Content = "Existing content", ParentId = null };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Reverse <script>example</script>", Content = "Music topic", ParentId = null };
        db.AddRange(category, question, old, point); await db.SaveChangesAsync();
        db.Add(new KnowledgePointQuestion { QuestionId = question.Id, KnowledgePointId = old.Id });
        question.Embedding = new byte[4096]; question.LastEmbeddedAt = question.LastUpdatedAt;
        await db.SaveChangesAsync();
        var url = $"/Questions/Edit/{question.Id}";
        var edit = await Http.GetStringAsync(url);
        Assert.Contains("data-knowledge-point-finder", edit);
        Assert.Contains(old.Id.ToString(), edit);
        var search = await Http.GetAsync("/ContentSearch/KnowledgePoints?query=Reverse&useAi=false"); search.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.IsFalse(json.RootElement.GetProperty("usedAi").GetBoolean());
        Assert.AreEqual(point.Id, json.RootElement.GetProperty("results")[0].GetProperty("id").GetGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync("/ContentSearch/RecommendedKnowledgePoints", new { id = question.Id })).StatusCode);
        var token = await GetAntiCsrfToken(url);
        using var recommend = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/RecommendedKnowledgePoints");
        recommend.Headers.Add("RequestVerificationToken", token); recommend.Content = JsonContent.Create(new { id = question.Id });
        (await Http.SendAsync(recommend)).EnsureSuccessStatusCode();
        Assert.AreEqual(old.Id, (await db.KnowledgePointQuestions.SingleAsync(q => q.QuestionId == question.Id)).KnowledgePointId);
        var form = new Dictionary<string, string>
        {
            ["Id"] = question.Id.ToString(), ["Content"] = "", ["QuestionType"] = "1", ["GradingStrategy"] = "0",
            ["CategoryId"] = category.Id.ToString(), ["UpdateKnowledgePointAssociations"] = "true",
            ["SelectedKnowledgePointIds"] = point.Id.ToString()
        };
        var invalid = await PostForm(url, form); invalid.EnsureSuccessStatusCode();
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.Contains(point.Id.ToString(), html); Assert.Contains("&lt;script&gt;example&lt;/script&gt;", html);
        Assert.AreEqual(old.Id, (await db.KnowledgePointQuestions.SingleAsync(q => q.QuestionId == question.Id)).KnowledgePointId);
        form["Content"] = question.Content;
        form["SelectedKnowledgePointIds"] = Guid.NewGuid().ToString();
        (await PostForm(url, form)).EnsureSuccessStatusCode();
        Assert.AreEqual(old.Id, (await db.KnowledgePointQuestions.SingleAsync(q => q.QuestionId == question.Id)).KnowledgePointId);
        form["SelectedKnowledgePointIds"] = point.Id.ToString();
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm(url, form)).StatusCode);
        db.ChangeTracker.Clear();
        Assert.AreEqual(point.Id, (await db.KnowledgePointQuestions.SingleAsync(q => q.QuestionId == question.Id)).KnowledgePointId);
        Assert.IsNotNull((await db.Questions.SingleAsync(q => q.Id == question.Id)).Embedding);
        form.Remove("SelectedKnowledgePointIds"); form.Remove("UpdateKnowledgePointAssociations");
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm(url, form)).StatusCode);
        Assert.AreEqual(1, await db.KnowledgePointQuestions.CountAsync(q => q.QuestionId == question.Id));
        form["UpdateKnowledgePointAssociations"] = "true";
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm(url, form)).StatusCode);
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync(q => q.QuestionId == question.Id));
    }

    [TestMethod]
    public async Task QuestionOnlyEditorCannotReadFinderOrForgeKnowledgePointAssociations()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var user = await manager.FindByEmailAsync(email);
        foreach (var permission in new[] { Authorization.AppPermissionNames.CanEditQuestions, Authorization.AppPermissionNames.CanReadQuestions })
            Assert.IsTrue((await manager.AddClaimAsync(user!, new System.Security.Claims.Claim(Authorization.AppPermissions.Type, permission))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var question = new Question { Id = Guid.NewGuid(), Content = "Limited editor", QuestionType = QuestionType.Blank, CategoryId = category.Id };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Keep relationship", Content = "Keep content", ParentId = null };
        db.AddRange(category, question, point); await db.SaveChangesAsync();
        db.Add(new KnowledgePointQuestion { QuestionId = question.Id, KnowledgePointId = point.Id }); await db.SaveChangesAsync();
        var url = $"/Questions/Edit/{question.Id}";
        Assert.DoesNotContain("data-knowledge-point-finder", await Http.GetStringAsync(url));
        var form = new Dictionary<string, string> { ["Id"] = question.Id.ToString(), ["Content"] = question.Content,
            ["QuestionType"] = "1", ["GradingStrategy"] = "0", ["CategoryId"] = category.Id.ToString() };
        Assert.AreEqual(HttpStatusCode.Found, (await PostForm(url, form)).StatusCode);
        foreach (var flag in new[] { "UpdateKnowledgePointAssociations", "SelectedKnowledgePointIds" })
        {
            form[flag] = flag == "UpdateKnowledgePointAssociations" ? "true" : point.Id.ToString();
            var denied = await PostForm(url, form);
            Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
            form.Remove(flag);
        }
        var searchDenied = await Http.GetAsync("/ContentSearch/KnowledgePoints?query=Keep&useAi=false");
        Assert.IsTrue(searchDenied.StatusCode == HttpStatusCode.Forbidden || searchDenied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        var token = await GetAntiCsrfToken(url);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/ContentSearch/RecommendedKnowledgePoints");
        request.Headers.Add("RequestVerificationToken", token); request.Content = JsonContent.Create(new { id = question.Id });
        var recommendationDenied = await Http.SendAsync(request);
        Assert.IsTrue(recommendationDenied.StatusCode == HttpStatusCode.Forbidden || recommendationDenied.Headers.Location!.ToString().Contains("/Error/Unauthorized"));
        Assert.AreEqual(1, await db.KnowledgePointQuestions.CountAsync(q => q.QuestionId == question.Id));
    }

    [TestMethod]
    public async Task QuestionReorderingPreservesEmbeddingAndReturnsToSearch()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var question = new Question { Id = Guid.NewGuid(), Content = "Boya", QuestionType = QuestionType.Blank, CategoryId = category.Id };
        db.AddRange(category, question); await db.SaveChangesAsync();
        question.Embedding = new byte[4096]; question.LastEmbeddedAt = question.LastUpdatedAt;
        await db.SaveChangesAsync();
        var response = await PostForm($"/Questions/Edit/{question.Id}", new Dictionary<string, string>
        {
            ["Id"] = question.Id.ToString(), ["Content"] = question.Content, ["QuestionType"] = "1",
            ["GradingStrategy"] = "0", ["CategoryId"] = category.Id.ToString(), ["OrderIndex"] = "10",
            ["ReturnUrl"] = "/ContentSearch?query=Boya"
        }, tokenUrl: $"/Questions/Edit/{question.Id}");
        AssertRedirect(response, "/ContentSearch?query=Boya");
        db.ChangeTracker.Clear();
        var saved = await db.Questions.SingleAsync(q => q.Id == question.Id);
        Assert.IsNotNull(saved.Embedding);
        Assert.AreEqual(10m, saved.OrderIndex);
    }
}
