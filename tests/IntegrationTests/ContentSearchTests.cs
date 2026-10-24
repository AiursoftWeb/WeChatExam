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
