using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PracticeTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public PracticeTests()
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
            "<input name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\" />");
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
    public async Task PracticeFlowTest()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category and a Question
        var categoryTitle = $"Practice-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        }));
        
        var catIndexResponse = await _http.GetAsync("/Categories/Index");
        var catIndexHtml = await catIndexResponse.Content.ReadAsStringAsync();
        var categoryId = Regex.Match(catIndexHtml, "href=\"/Categories/Details/([^\"]+)\"").Groups[1].Value;

        var qText = $"Practice-Question-{Guid.NewGuid()}";
        var qToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" }, // Choice
            { "GradingStrategy", "0" }, // Standard
            { "CategoryId", categoryId },
            { "Options[0]", "Option A" },
            { "Options[1]", "Option B" },
            { "StandardAnswer", "Option A" },
            { "Explanation", "Explanation here" },
            { "__RequestVerificationToken", qToken }
        }));

        var qIndexResponse = await _http.GetAsync($"/Questions/Index?categoryId={categoryId}");
        var qIndexHtml = await qIndexResponse.Content.ReadAsStringAsync();
        var questionId = Regex.Match(qIndexHtml, "href=\"/Questions/Details/([^\"]+)\"").Groups[1].Value;

        // 2. Access Practice Index
        var practiceIndexResponse = await _http.GetAsync("/Practice/Index");
        practiceIndexResponse.EnsureSuccessStatusCode();
        var practiceIndexHtml = await practiceIndexResponse.Content.ReadAsStringAsync();
        Assert.Contains(qText, practiceIndexHtml);

        // 3. Test MTQL Filter
        var mtqlResponse = await _http.GetAsync($"/Practice/Index?mtql=content:\"{qText}\"");
        mtqlResponse.EnsureSuccessStatusCode();
        var mtqlHtml = await mtqlResponse.Content.ReadAsStringAsync();
        Assert.Contains(qText, mtqlHtml);

        // 4. Start Practice
        var startToken = await GetAntiCsrfToken("/Practice/Index");
        var startContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "questionIds[0]", questionId },
            { "__RequestVerificationToken", startToken }
        });
        var startResponse = await _http.PostAsync("/Practice/Start", startContent);
        Assert.AreEqual(HttpStatusCode.OK, startResponse.StatusCode);
        var practiceHtml = await startResponse.Content.ReadAsStringAsync();
        Assert.Contains("Practice Session", practiceHtml);
        Assert.Contains(qText, practiceHtml);

        // 5. Grade Answer
        var gradeContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "questionId", questionId },
            { "userAnswer", "Option A" }
        });
        var gradeResponse = await _http.PostAsync("/Practice/Grade", gradeContent);
        gradeResponse.EnsureSuccessStatusCode();
        var gradeJson = await gradeResponse.Content.ReadAsStringAsync();
        Assert.Contains("\"isCorrect\":true", gradeJson);
        Assert.Contains("\"score\":10", gradeJson);
    }
}
