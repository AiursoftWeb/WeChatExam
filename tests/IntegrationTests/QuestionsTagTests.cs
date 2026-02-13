using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionsTagTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public QuestionsTagTests()
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
    }

    [TestMethod]
    public async Task CreateAndEditQuestionWithTagsTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Category
        var categoryTitle = $"Test-Category-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        var catContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        });
        
        var catResponse = await _http.PostAsync("/Categories/Create", catContent);
        var catDetailsUrl = catResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(catDetailsUrl);
        var categoryId = catDetailsUrl.Split('/').Last().Split('?')[0];

        // 2. Create Question with Tags (Chinese)
        var qText = $"Test Question {Guid.NewGuid()}";
        var tags = "贝多芬 钢琴 奏鸣曲"; 
        
        var createToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" }, // Choice
            { "GradingStrategy", "0" }, // AllOrNothing
            { "CategoryId", categoryId },
            { "Tags", tags },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "Explanation", "Explanation" },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/Questions/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        var questionId = detailsUrl.Split('/').Last().Split('?')[0];

        // 3. Get Edit Page and Verify Tags Rendering
        var editUrl = $"/Questions/Edit/{questionId}";
        var editResponse = await _http.GetAsync(editUrl);
        editResponse.EnsureSuccessStatusCode();
        var editHtml = await editResponse.Content.ReadAsStringAsync();

        Assert.IsFalse(editHtml.Contains("'.split(' ').filter(t => t.trim());"), "The old split/filter logic should be gone.");
        Assert.IsTrue(editHtml.Contains("TagInput.init(tagInput, tagHidden, initialTags);"));
        Assert.IsTrue(editHtml.Contains("const initialTags = ["), "Should contain JSON array assignment");
    }

    [TestMethod]
    public async Task CreatePageRenderingTest()
    {
        await LoginAsAdminAsync();

        // 1. Get Create Page
        var createResponse = await _http.GetAsync("/Questions/Create");
        createResponse.EnsureSuccessStatusCode();
        var createHtml = await createResponse.Content.ReadAsStringAsync();

        Assert.IsTrue(createHtml.Contains("const initialTags = ["), "Should contain JSON array assignment (likely empty)");
        Assert.IsTrue(createHtml.Contains("TagInput.init(tagInput, tagHidden, initialTags);"), "Should pass initialTags to init");
        Assert.IsFalse(createHtml.Contains("TagInput.init(tagInput, tagHidden, []);"), "Should not pass empty array literal directly");
    }
}
