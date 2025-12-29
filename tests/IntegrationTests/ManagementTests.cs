using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ManagementTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ManagementTests()
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
        _server = await AppAsync<Startup>([], port: _port);
        await _server.UpdateDbAsync<TemplateDbContext>();
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
        var email = "admin@default.com"; // Default admin email from SeedAsync
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
        Assert.AreEqual("/Dashboard/Index", loginResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task CategoriesCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Category
        var categoryTitle = $"Test-Category-{Guid.NewGuid()}";
        var createToken = await GetAntiCsrfToken("/Categories/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createToken }
        });
        
        var createResponse = await _http.PostAsync("/Categories/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        Assert.StartsWith("/Categories/Details", detailsUrl);
        
        var categoryId = detailsUrl.Split('/').Last().Split('?')[0]; // Extract ID

        // 2. Read Category (Details)
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, detailsHtml);

        // 3. Edit Category
        var newTitle = $"Updated-Category-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/Categories/Edit/{categoryId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", categoryId },
            { "Title", newTitle },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Categories/Edit/{categoryId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync(detailsUrl);
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newTitle, verifyHtml);

        // 4. Delete Category
        var deleteToken = await GetAntiCsrfToken($"/Categories/Delete/{categoryId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Categories/Delete/{categoryId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Categories/Index" || location == "/Categories");

        // Verify Deletion
        var indexResponse = await _http.GetAsync("/Categories/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newTitle, indexHtml);
    }

    [TestMethod]
    public async Task KnowledgePointsCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create KnowledgePoint
        var title = $"Test-KP-{Guid.NewGuid()}";
        var content = "This is a test knowledge point content.";
        var createToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", title },
            { "Content", content },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/KnowledgePoints/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        
        var kpId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read KnowledgePoint
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(title, detailsHtml);
        Assert.Contains(content, detailsHtml);

        // 3. Edit KnowledgePoint
        var newTitle = $"Updated-KP-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/KnowledgePoints/Edit/{kpId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", kpId },
            { "Title", newTitle },
            { "Content", content },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/KnowledgePoints/Edit/{kpId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Delete KnowledgePoint
        var deleteToken = await GetAntiCsrfToken($"/KnowledgePoints/Delete/{kpId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/KnowledgePoints/Delete/{kpId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/KnowledgePoints/Index" || location == "/KnowledgePoints");
    }

    [TestMethod]
    public async Task QuestionsCrudTest()
    {
        await LoginAsAdminAsync();

        // Prerequisite: Create a Category first
        var categoryTitle = $"Q-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        var catContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        });
        var catResponse = await _http.PostAsync("/Categories/Create", catContent);
        var catDetailsUrl = catResponse.Headers.Location?.OriginalString;
        var categoryId = catDetailsUrl!.Split('/').Last().Split('?')[0];

        // 1. Create Question
        var qText = $"Test Question {Guid.NewGuid()}";
        var qType = "0";
        var qStrategy= "0";
        var createToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", qType },
            { "GradingStrategy", qStrategy },
            { "CategoryId", categoryId },
            { "Metadata", "[\"A\", \"B\"]" },
            { "StandardAnswer", "A" },
            { "Explanation", "A is correct" },
            { "__RequestVerificationToken", createToken }
        });
        var createResponse = await _http.PostAsync("/Questions/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        var questionId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read Question
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(qText, detailsHtml);

        // 3. Edit Question
        var newText = $"Updated Question {Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/Questions/Edit/{questionId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", questionId },
            { "Type", qType },
            { "Text", newText },
            { "CategoryId", categoryId },
            { "List", "[\"A\", \"B\"]" },
            { "SingleCorrect", "B" }, // Changed answer
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Questions/Edit/{questionId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Delete Question
        var deleteToken = await GetAntiCsrfToken($"/Questions/Delete/{questionId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Questions/Delete/{questionId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
    }

    [TestMethod]
    public async Task UsersCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create User
        var userName = $"testuser{Guid.NewGuid().ToString().Substring(0, 8)}";
        var email = $"{userName}@test.com";
        var password = "TestPassword123!";
        var displayName = "Test User Display";
        
        var createToken = await GetAntiCsrfToken("/Users/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "UserName", userName },
            { "DisplayName", displayName },
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/Users/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        var userId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read User (Details)
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(userName, detailsHtml);
        Assert.Contains(email, detailsHtml);

        // 3. Edit User
        var newDisplayName = "Updated Display Name";
        var editToken = await GetAntiCsrfToken($"/Users/Edit/{userId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", userId },
            { "UserName", userName },
            { "Email", email },
            { "DisplayName", newDisplayName },
            { "AvatarUrl", "Workspace/avatar/default-avatar.jpg" },
            { "Password", "you-cant-read-it" }, // Keep password
            { "__RequestVerificationToken", editToken }
        });

        var editResponse = await _http.PostAsync($"/Users/Edit/{userId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync(detailsUrl);
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newDisplayName, verifyHtml);

        // 4. Delete User
        var deleteToken = await GetAntiCsrfToken($"/Users/Delete/{userId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        
        var deleteResponse = await _http.PostAsync($"/Users/Delete/{userId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Users/Index" || location == "/Users");

        // Verify Deletion
        var indexResponse = await _http.GetAsync("/Users/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(userName, indexHtml);
    }
}
