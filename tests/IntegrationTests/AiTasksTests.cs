using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using Newtonsoft.Json.Linq;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class AiTasksTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private StubOllamaService _mockOllamaService = new();

    public AiTasksTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _http = new HttpClient(handler);
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockOllamaService = _mockOllamaService;
        TestStartupWithMockWeChat.MockWeChatService = new StubWeChatService();
        TestStartupWithMockWeChat.MockDistributionChannelService = new StubDistributionChannelService();
        
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(_server);
            await _server.UpdateDbAsync<WeChatExamDbContext>();
            await _server.SeedAsync();
            await _server.StartAsync();
        }
        catch
        {
            _server.Dispose();
            throw;
        }
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
        var password = "Admin@123456!";

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
    [DataRow(true)]
    [DataRow(false)]
    public async Task AutoCategorizeTaskTest(bool returnId)
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();

        // 1. Create Categories
        var categoryTitle = $"AutoCat-{Guid.NewGuid()}";
        var newCategoryTitle = $"New-AutoCat-{Guid.NewGuid()}";
        
        using var scope = _server!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = categoryTitle, ParentId = null };
        var newCategory = new Category { Id = Guid.NewGuid(), Title = newCategoryTitle, ParentId = null };
        dbContext.AddRange(category, newCategory);
        await dbContext.SaveChangesAsync();

        // 2. Create Question in first category
        var qText = $"Test-Question-{Guid.NewGuid()}";
        var createQToken = await GetAntiCsrfToken($"/Questions/Create?questionCategoryId={periodId}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" },
            { "GradingStrategy", "0" },
            { "QuestionCategoryId", periodId.ToString() },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "Explanation", "Explanation" },
            { "__RequestVerificationToken", createQToken }
        }));

        var question = dbContext.Questions.FirstOrDefault(q => q.Content == qText);
        Assert.IsNotNull(question);

        var suggestedPeriodId = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var suggestedPeriod = await dbContext.QuestionCategories.Include(c => c.TotalCategory).SingleAsync(c => c.Id == suggestedPeriodId);
        suggestedPeriod.Title = "Unique suggestion " + Guid.NewGuid();
        await dbContext.SaveChangesAsync();
        var expectedName = $"{suggestedPeriod.TotalCategory!.Title} / {suggestedPeriod.Title}";
        // AI candidates are periods. Legacy knowledge categories must not leak into the prompt.
        _mockOllamaService.Response = returnId ? suggestedPeriodId.ToString() : expectedName;

        // 4. Trigger AutoCategorize
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { questionIds = new[] { question.Id } }), System.Text.Encoding.UTF8, "application/json");
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
                Assert.AreEqual(expectedName, item?["newValue"]?.ToString());
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
        Assert.AreEqual(suggestedPeriodId, question.QuestionCategoryId);
        Assert.IsNull(question.CategoryId);
        StringAssert.Contains(_mockOllamaService.Questions.Single(), expectedName);
        Assert.DoesNotContain(category.Id.ToString(), _mockOllamaService.Questions.Single());
        Assert.DoesNotContain(newCategory.Id.ToString(), _mockOllamaService.Questions.Single());
    }

    [TestMethod]
    [DataRow("GenerateExplanations")]
    [DataRow("GenerateAnswers")]
    [DataRow("AutoTagging")]
    public async Task AiPromptsUsePeriodContextInsteadOfKnowledgeCategory(string action)
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var period = await db.QuestionCategories.Include(c => c.TotalCategory).SingleAsync(c => c.Id == periodId);
        var legacy = new Category { Title = "Legacy context " + Guid.NewGuid(), ParentId = null };
        var question = new Question { Content = "Question context", StandardAnswer = "Answer", QuestionCategoryId = periodId, Category = legacy };
        db.Questions.Add(question); await db.SaveChangesAsync();
        _mockOllamaService.Response = "Suggested answer";
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", await GetAntiCsrfToken("/Questions/Index"));
        var response = await _http.PostAsync("/AiTasks/" + action,
            new StringContent(System.Text.Json.JsonSerializer.Serialize(new { questionIds = new[] { question.Id } }), System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var taskId = JObject.Parse(await response.Content.ReadAsStringAsync())["taskId"]!.ToObject<Guid>();
        var task = scope.ServiceProvider.GetRequiredService<AiTaskService>().GetTask(taskId)!;
        for (var attempt = 0; attempt < 40 && !task.IsCompleted; attempt++) await Task.Delay(250);
        Assert.IsTrue(task.IsCompleted);
        var prompt = _mockOllamaService.Questions.Single();
        StringAssert.Contains(prompt, $"{period.TotalCategory!.Title} / {period.Title}");
        Assert.DoesNotContain(legacy.Title, prompt);
    }

    [TestMethod]
    public async Task PeriodAdoptionRejectsInvalidIdsWithoutPartialChanges()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var oldPeriod = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var newPeriod = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var deletedPeriod = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        db.QuestionCategories.Remove(await db.QuestionCategories.SingleAsync(c => c.Id == deletedPeriod));
        var legacy = new Category { Title = "Legacy knowledge tree", ParentId = null };
        var first = new Question { Content = "First adoption question", QuestionCategoryId = oldPeriod, Category = legacy };
        var second = new Question { Content = "Second adoption question", QuestionCategoryId = oldPeriod, Category = legacy };
        db.Questions.AddRange(first, second);
        await db.SaveChangesAsync();
        var tasks = scope.ServiceProvider.GetRequiredService<AiTaskService>();
        var firstItem = new AiTaskItem { QuestionId = first.Id, Status = AiTaskStatus.Completed, NewEntityId = newPeriod };
        var secondItem = new AiTaskItem { QuestionId = second.Id, Status = AiTaskStatus.Completed };
        var task = tasks.CreateTask([firstItem, secondItem], AiTaskType.AutoCategorize);
        var token = await GetAntiCsrfToken($"/AiTasks/Preview?taskId={task.Id}");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", token);

        foreach (var invalid in new Guid?[] { null, Guid.Empty, Guid.NewGuid(), deletedPeriod, legacy.Id })
        {
            secondItem.NewEntityId = invalid;
            var single = await _http.PostAsync($"/AiTasks/Adopt?taskId={task.Id}&questionId={second.Id}", null);
            Assert.AreEqual(HttpStatusCode.BadRequest, single.StatusCode);
            var batch = await _http.PostAsync($"/AiTasks/AdoptAll?taskId={task.Id}", null);
            Assert.AreEqual(HttpStatusCode.BadRequest, batch.StatusCode);
            var edited = await _http.PostAsync($"/AiTasks/Edit?taskId={task.Id}&questionId={second.Id}",
                new StringContent(System.Text.Json.JsonSerializer.Serialize(invalid?.ToString() ?? "not-a-guid"), System.Text.Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.BadRequest, edited.StatusCode);
            db.ChangeTracker.Clear();
            Assert.AreEqual(oldPeriod, (await db.Questions.SingleAsync(q => q.Id == first.Id)).QuestionCategoryId);
            Assert.AreEqual(oldPeriod, (await db.Questions.SingleAsync(q => q.Id == second.Id)).QuestionCategoryId);
            Assert.AreEqual(2, task.Items.Count);
            Assert.IsFalse(task.IsCanceled);
        }

        // A valid ID is still not adoptable while the AI item is unfinished.
        secondItem.NewEntityId = newPeriod;
        secondItem.Status = AiTaskStatus.Processing;
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await _http.PostAsync($"/AiTasks/Adopt?taskId={task.Id}&questionId={second.Id}", null)).StatusCode);
        secondItem.Status = AiTaskStatus.Completed;
        Assert.AreEqual(HttpStatusCode.OK, (await _http.PostAsync($"/AiTasks/AdoptAll?taskId={task.Id}", null)).StatusCode);
        db.ChangeTracker.Clear();
        foreach (var id in new[] { first.Id, second.Id })
        {
            var question = await db.Questions.SingleAsync(q => q.Id == id);
            Assert.AreEqual(newPeriod, question.QuestionCategoryId);
            Assert.AreEqual(legacy.Id, question.CategoryId, "Adoption must not rewrite the legacy relationship.");
        }
        Assert.IsEmpty(task.Items);
        Assert.IsTrue(task.IsCanceled);

        var editTask = tasks.CreateTask([new AiTaskItem { QuestionId = first.Id }], AiTaskType.AutoCategorize);
        Assert.AreEqual(HttpStatusCode.OK, (await _http.PostAsync($"/AiTasks/Edit?taskId={editTask.Id}&questionId={first.Id}",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(oldPeriod.ToString()), System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        db.ChangeTracker.Clear();
        Assert.AreEqual(oldPeriod, (await db.Questions.SingleAsync(q => q.Id == first.Id)).QuestionCategoryId);
        Assert.IsEmpty(editTask.Items);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AiCannotSelectLegacyCategoryOrAmbiguousPeriod(bool ambiguousName)
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var first = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var second = await QuestionPeriodAssignmentTests.AddPeriod(_server.Services);
        var title = "Shared period " + Guid.NewGuid();
        (await db.QuestionCategories.SingleAsync(c => c.Id == first)).Title = title;
        (await db.QuestionCategories.SingleAsync(c => c.Id == second)).Title = title;
        var legacy = new Category { Title = "Not an AI candidate", ParentId = null };
        var question = new Question { Content = "Ambiguous period question", QuestionCategoryId = first, Category = legacy };
        db.Questions.Add(question); await db.SaveChangesAsync();
        _mockOllamaService.Response = ambiguousName ? title : legacy.Id.ToString();
        var token = await GetAntiCsrfToken("/Questions/Index");
        _http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var response = await _http.PostAsync("/AiTasks/AutoCategorize",
            new StringContent(System.Text.Json.JsonSerializer.Serialize(new { questionIds = new[] { question.Id } }), System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var id = JObject.Parse(await response.Content.ReadAsStringAsync())["taskId"]!.ToObject<Guid>();
        var task = scope.ServiceProvider.GetRequiredService<AiTaskService>().GetTask(id)!;
        for (var attempt = 0; attempt < 40 && !task.IsCompleted; attempt++) await Task.Delay(250);
        Assert.AreEqual(AiTaskStatus.Failed, task.Items[question.Id].Status);
        Assert.IsNull(task.Items[question.Id].NewEntityId);
        db.ChangeTracker.Clear();
        Assert.AreEqual(first, (await db.Questions.SingleAsync(q => q.Id == question.Id)).QuestionCategoryId);
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
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
            Content = qText,
            QuestionType = QuestionType.Choice,
            StandardAnswer = "A",
            Explanation = "Old explanation"
        };
        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        // 2. Mock Ollama response with <tag> tags in new format
        _mockOllamaService.Response = "Style: <tag>Jazz</tag> <tag>Classical</tag>";

        // 3. Trigger AutoTagging
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { questionIds = new[] { question.Id } }), System.Text.Encoding.UTF8, "application/json");
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
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
            Content = qText,
            QuestionType = QuestionType.Choice,
            StandardAnswer = "",
            Explanation = "Old explanation"
        };
        dbContext.Questions.Add(question);
        await dbContext.SaveChangesAsync();

        // 2. Mock Ollama response 
        var suggestedAnswer = "Suggested Answer";
        _mockOllamaService.Response = suggestedAnswer;

        // 3. Trigger GenerateAnswer
        var token = await GetAntiCsrfToken("/Questions/Index");
        var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { questionIds = new[] { question.Id } }), System.Text.Encoding.UTF8, "application/json");
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
