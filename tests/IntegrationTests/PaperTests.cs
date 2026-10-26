using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaperTests
{
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
        _http = new HttpClient(handler);
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<Startup>([], port: reservation.Port);
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
        var email = "admin@default.com"; // Default admin email from SeedAsync
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
        Assert.AreEqual("/Dashboard/Index", loginResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task PaperMtqlBatchAddTest()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();

        // 2. Create some questions
        var questionIds = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var qText = $"MTQL Test Question {i} {Guid.NewGuid()}";
            var createToken = await GetAntiCsrfToken($"/Questions/Create?questionCategoryId={periodId}");
            var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "Content", qText},
                { "QuestionType", "0" },
                { "GradingStrategy", "0" },
                { "QuestionCategoryId", periodId.ToString() },
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

    [TestMethod]
    public async Task PaperRealExamAndTagsTest()
    {
        await LoginAsAdminAsync();

        // 1. Create a Paper with IsRealExam and Tags
        var paperTitle = $"Real Exam Paper {Guid.NewGuid()}";
        var paperToken = await GetAntiCsrfToken("/Papers/Create");
        var paperContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", paperTitle },
            { "TimeLimit", "120" },
            { "IsFree", "true" },
            { "IsRealExam", "true" },
            { "Tags", "RealTag1 RealTag2" },
            { "__RequestVerificationToken", paperToken }
        });
        var paperResponse = await _http.PostAsync("/Papers/Create", paperContent);
        Assert.AreEqual(HttpStatusCode.Found, paperResponse.StatusCode);
        var paperEditUrl = paperResponse.Headers.Location?.OriginalString;
        var paperId = paperEditUrl!.Split('/').Last().Split('?')[0];

        // 2. Verify Details
        var detailsResponse = await _http.GetAsync($"/Papers/Details/{paperId}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(detailsHtml.Contains("Real Exam"), "Should show Real Exam label");
        Assert.IsTrue(detailsHtml.Contains("Yes"), "Should show Yes for Real Exam");
        Assert.IsTrue(detailsHtml.Contains("RealTag1"), "Should show RealTag1");
        Assert.IsTrue(detailsHtml.Contains("RealTag2"), "Should show RealTag2");

        // 3. Edit Paper (Change IsRealExam and Tags)
        var editToken = await GetAntiCsrfToken(paperEditUrl);
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", paperId },
            { "Title", paperTitle + " Updated" },
            { "TimeLimit", "90" },
            { "IsFree", "false" },
            { "IsRealExam", "false" },
            { "Tags", "UpdatedTag" },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync(paperEditUrl, editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Verify Updated Details
        var updatedDetailsResponse = await _http.GetAsync($"/Papers/Details/{paperId}");
        updatedDetailsResponse.EnsureSuccessStatusCode();
        var updatedHtml = await updatedDetailsResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(updatedHtml.Contains("No"), "Should show No for Real Exam");
        Assert.IsTrue(updatedHtml.Contains("UpdatedTag"), "Should show UpdatedTag");
        Assert.IsFalse(updatedHtml.Contains("RealTag1"), "Should not show old RealTag1");
    }
}
