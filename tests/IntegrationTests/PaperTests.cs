using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaperTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public PaperTests()
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
        Assert.AreEqual("/Dashboard/Index", loginResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task PaperMtqlBatchAddTest()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = $"Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        var catContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        });
        var catResponse = await _http.PostAsync("/Categories/Create", catContent);
        var catDetailsUrl = catResponse.Headers.Location?.OriginalString;
        var categoryId = catDetailsUrl!.Split('/').Last().Split('?')[0];

        // 2. Create some questions
        var questionIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var qText = $"MTQL Test Question {i} {Guid.NewGuid()}";
            var createToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
            var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "Content", qText},
                { "QuestionType", "0" },
                { "GradingStrategy", "0" },
                { "CategoryId", categoryId },
                { "Options[0]", "A" },
                { "Options[1]", "B" },
                { "StandardAnswer", "A" },
                { "Tags", "MTQLTestTag" },
                { "__RequestVerificationToken", createToken }
            });
            var createResponse = await _http.PostAsync("/Questions/Create", createContent);
            var qDetailsUrl = createResponse.Headers.Location?.OriginalString;
            questionIds.Add(qDetailsUrl!.Split('/').Last().Split('?')[0]);
        }

        // 3. Create a Paper
        var paperTitle = $"Test Paper {Guid.NewGuid()}";
        var paperToken = await GetAntiCsrfToken("/Papers/Create");
        var paperContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", paperTitle },
            { "TimeLimit", "60" },
            { "IsFree", "false" },
            { "__RequestVerificationToken", paperToken }
        });
        var paperResponse = await _http.PostAsync("/Papers/Create", paperContent);
        var paperEditUrl = paperResponse.Headers.Location?.OriginalString;
        var paperId = paperEditUrl!.Split('/').Last().Split('?')[0];

        // 4. Test SearchQuestions (MTQL)
        var mtql = "MTQLTestTag";
        var searchResponse = await _http.GetAsync($"/Papers/SearchQuestions/{paperId}?mtql={Uri.EscapeDataString(mtql)}");
        searchResponse.EnsureSuccessStatusCode();
        var searchJson = await searchResponse.Content.ReadAsStringAsync();
        foreach (var qId in questionIds)
        {
            Assert.IsTrue(searchJson.Contains(qId, StringComparison.OrdinalIgnoreCase), $"Search result should contain question ID {qId}");
        }

        // 5. Test BatchAddQuestions
        var batchAddToken = await GetAntiCsrfToken(paperEditUrl);
        var batchAddContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("questionIds", questionIds[0]),
            new KeyValuePair<string, string>("questionIds", questionIds[1]),
            new KeyValuePair<string, string>("startingOrder", "1"),
            new KeyValuePair<string, string>("defaultScore", "10"),
            new KeyValuePair<string, string>("__RequestVerificationToken", batchAddToken)
        });
        var batchAddResponse = await _http.PostAsync($"/Papers/BatchAddQuestions/{paperId}", batchAddContent);
        Assert.AreEqual(HttpStatusCode.Found, batchAddResponse.StatusCode);

        // 6. Verify questions added to paper
        var detailsResponse = await _http.GetAsync($"/Papers/Details/{paperId}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(detailsHtml.Contains("<dd class=\"col-sm-8\">2</dd>"), "Paper details should show 2 questions");
    }
}
