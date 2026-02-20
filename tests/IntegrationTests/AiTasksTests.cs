using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Moq;
using Newtonsoft.Json.Linq;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class AiTasksTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private Mock<IOllamaService> _mockOllamaService = new();

    public AiTasksTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockOllamaService = _mockOllamaService;
        TestStartupWithMockWeChat.MockWeChatService = new Mock<IWeChatService>(); 
        TestStartupWithMockWeChat.MockDistributionChannelService = new Mock<IDistributionChannelService>();
        
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    private async Task LoginAsAdminAsync()
    {
        var email = "admin@default.com";
        var password = "admin123";

        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task AutoCategorizeTaskTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Categories
        var categoryTitle = $"AutoCat-{Guid.NewGuid()}";
        var newCategoryTitle = $"New-AutoCat-{Guid.NewGuid()}";
        
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        }));
        
        catToken = await GetAntiCsrfToken("/Categories/Create"); // Refresh token
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", newCategoryTitle },
            { "__RequestVerificationToken", catToken }
        }));
        
        using var scope = _server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = dbContext.Categories.FirstOrDefault(c => c.Title == categoryTitle);
        var newCategory = dbContext.Categories.FirstOrDefault(c => c.Title == newCategoryTitle);
        Assert.IsNotNull(category);
        Assert.IsNotNull(newCategory);

        // 2. Create Question in first category
        var qText = $"Test-Question-{Guid.NewGuid()}";
        var createQToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={category.Id}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" },
            { "GradingStrategy", "0" },
            { "CategoryId", category.Id.ToString() },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "Explanation", "Explanation" },
            { "__RequestVerificationToken", createQToken }
        }));

        var question = dbContext.Questions.FirstOrDefault(q => q.Content == qText);
        Assert.IsNotNull(question);

        // 3. Mock Ollama response to return the ID of the new category
        _mockOllamaService.Setup(s => s.AskQuestion(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newCategory.Id.ToString());

        // 4. Trigger AutoCategorize
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new[] { question.Id }), System.Text.Encoding.UTF8, "application/json");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        
        var response = await _http.PostAsync("/AiTasks/AutoCategorize", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var resultJson = await response.Content.ReadAsStringAsync();
        var jsonObj = JObject.Parse(resultJson);
        Assert.IsNotNull(jsonObj);
        var taskId = jsonObj["taskId"]?.ToString();
        Assert.IsNotNull(taskId);

        // 5. Poll for completion
        var isCompleted = false;
        for (int i = 0; i < 20; i++) // Wait up to 10 seconds
        {
            var statusResponse = await _http.GetAsync($"/AiTasks/GetStatus?taskId={taskId}");
            statusResponse.EnsureSuccessStatusCode();
            var statusJson = await statusResponse.Content.ReadAsStringAsync();
            var statusObj = JObject.Parse(statusJson);
            
            if (statusObj["isCompleted"]?.ToObject<bool>() == true)
            {
                var items = statusObj["items"] as JArray;
                var item = items?[0];
                Assert.AreEqual(newCategory.Title, item?["newValue"]?.ToString());
                Assert.AreEqual("Completed", item?["statusText"]?.ToString());
                isCompleted = true;
                break;
            }
            await Task.Delay(500);
        }
        Assert.IsTrue(isCompleted, "AI Task did not complete in time.");

        // 6. Adopt the suggestion
        var adoptToken = await GetAntiCsrfToken($"/AiTasks/Preview?taskId={taskId}");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", adoptToken);
        
        var adoptResponse = await _http.PostAsync($"/AiTasks/Adopt?taskId={taskId}&questionId={question.Id}", null);
        Assert.AreEqual(HttpStatusCode.OK, adoptResponse.StatusCode);

        // 7. Verify Question Category Updated
        // Need to reload question from DB
        dbContext.Entry(question).Reload();
        Assert.AreEqual(newCategory.Id, question.CategoryId);
    }

    [TestMethod]
    public async Task AutoTaggingTaskTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Taxonomy and Question
        using var scope = _server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        
        var taxonomy = new Taxonomy { Name = "Style" };
        dbContext.Taxonomies.Add(taxonomy);
        await dbContext.SaveChangesAsync();

        var qText = $"Test-Question-Tagging-{Guid.NewGuid()}";
        var question = new Question
        {
            Content = qText,
            QuestionType = QuestionType.Choice,
            StandardAnswer = "A",
            Explanation = "Old explanation"
        };
        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        // 2. Mock Ollama response with <tag> tags in new format
        _mockOllamaService.Setup(s => s.AskQuestion(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Style: <tag>Jazz</tag> <tag>Classical</tag>");

        // 3. Trigger AutoTagging
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new[] { question.Id }), System.Text.Encoding.UTF8, "application/json");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        
        var response = await _http.PostAsync("/AiTasks/AutoTagging", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var resultJson = await response.Content.ReadAsStringAsync();
        var jsonObj = JObject.Parse(resultJson);
        var taskId = jsonObj["taskId"]?.ToString();
        Assert.IsNotNull(taskId);

        // 4. Poll for completion
        var isCompleted = false;
        for (int i = 0; i < 20; i++)
        {
            var statusResponse = await _http.GetAsync($"/AiTasks/GetStatus?taskId={taskId}");
            var statusJson = await statusResponse.Content.ReadAsStringAsync();
            var statusObj = JObject.Parse(statusJson);
            
            if (statusObj["isCompleted"]?.ToObject<bool>() == true)
            {
                var items = statusObj["items"] as JArray;
                var item = items?[0];
                Assert.IsTrue(item?["newValue"]?.ToString().Contains("Jazz") == true);
                Assert.IsTrue(item?["newValue"]?.ToString().Contains("Classical") == true);
                isCompleted = true;
                break;
            }
            await Task.Delay(500);
        }
        Assert.IsTrue(isCompleted);

        // 5. Adopt
        var adoptToken = await GetAntiCsrfToken($"/AiTasks/Preview?taskId={taskId}");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", adoptToken);
        
        var adoptResponse = await _http.PostAsync($"/AiTasks/Adopt?taskId={taskId}&questionId={question.Id}", null);
        Assert.AreEqual(HttpStatusCode.OK, adoptResponse.StatusCode);

        // 6. Verify Tags in DB
        var tags = await dbContext.QuestionTags
            .Where(qt => qt.QuestionId == question.Id)
            .Include(qt => qt.Tag)
            .Select(qt => qt.Tag.DisplayName)
            .ToListAsync();
        
        Assert.IsTrue(tags.Contains("Jazz"));
        Assert.IsTrue(tags.Contains("Classical"));
        
        var jazzTag = await dbContext.Tags.FirstOrDefaultAsync(t => t.DisplayName == "Jazz");
        Assert.AreEqual(taxonomy.Id, jazzTag?.TaxonomyId);
    }

    [TestMethod]
    public async Task GenerateAnswerTaskTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Question without answer
        using var scope = _server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        
        var qText = $"Test-Question-Answer-{Guid.NewGuid()}";
        var question = new Question
        {
            Content = qText,
            QuestionType = QuestionType.Choice,
            StandardAnswer = "",
            Explanation = "Old explanation"
        };
        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        // 2. Mock Ollama response 
        var suggestedAnswer = "Suggested Answer";
        _mockOllamaService.Setup(s => s.AskQuestion(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(suggestedAnswer);

        // 3. Trigger GenerateAnswer
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new[] { question.Id }), System.Text.Encoding.UTF8, "application/json");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        
        var response = await _http.PostAsync("/AiTasks/GenerateAnswers", content);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        
        var resultJson = await response.Content.ReadAsStringAsync();
        var jsonObj = JObject.Parse(resultJson);
        var taskId = jsonObj["taskId"]?.ToString();
        Assert.IsNotNull(taskId);

        // 4. Poll for completion
        var isCompleted = false;
        for (int i = 0; i < 20; i++)
        {
            var statusResponse = await _http.GetAsync($"/AiTasks/GetStatus?taskId={taskId}");
            var statusJson = await statusResponse.Content.ReadAsStringAsync();
            var statusObj = JObject.Parse(statusJson);
            
            if (statusObj["isCompleted"]?.ToObject<bool>() == true)
            {
                var items = statusObj["items"] as JArray;
                var item = items?[0];
                Assert.AreEqual(suggestedAnswer, item?["newValue"]?.ToString());
                isCompleted = true;
                break;
            }
            await Task.Delay(500);
        }
        Assert.IsTrue(isCompleted);

        // 5. Adopt
        var adoptToken = await GetAntiCsrfToken($"/AiTasks/Preview?taskId={taskId}");
        _http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", adoptToken);
        
        var adoptResponse = await _http.PostAsync($"/AiTasks/Adopt?taskId={taskId}&questionId={question.Id}", null);
        Assert.AreEqual(HttpStatusCode.OK, adoptResponse.StatusCode);

        // 6. Verify Answer in DB
        await dbContext.Entry(question).ReloadAsync();
        Assert.AreEqual(suggestedAnswer, question.StandardAnswer);
    }
}
