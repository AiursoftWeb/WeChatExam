using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;
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

    private async Task<Guid> SeedLegacyPaper(string title, bool real = false)
    {
        using var scope = _server!.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPaperService>();
        var paper = await service.CreatePaperAsync(title, real ? 120 : 60, real, real);
        if (real)
        {
            var tags = scope.ServiceProvider.GetRequiredService<ITagService>();
            foreach (var name in new[] { "RealTag1", "RealTag2" })
                await tags.AddTagToPaperAsync(paper.Id, (await tags.GetOrCreateTagAsync(name)).Id);
        }
        return paper.Id;
    }

    [TestMethod]
    public async Task LegacyCreationIsRetiredWhileArchiveAndNewEditorsRemainAvailable()
    {
        var paperId = await SeedLegacyPaper("Existing archived paper");
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var count = await db.Papers.CountAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync("/Papers/Create")).StatusCode);
        await LoginAsAdminAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync("/Papers/Create")).StatusCode);
        using var submitted = await _http.PostAsync("/Papers/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Must not create an old paper", ["TimeLimit"] = "180",
            ["__RequestVerificationToken"] = await GetAntiCsrfToken($"/Papers/Edit/{paperId}")
        }));
        Assert.AreEqual(HttpStatusCode.NotFound, submitted.StatusCode);
        Assert.AreEqual(count, await db.Papers.CountAsync());
        var html = WebUtility.HtmlDecode(await _http.GetStringAsync("/Papers"));
        StringAssert.Contains(html, "Legacy Paper Archive");
        StringAssert.Contains(html, "Existing archived paper");
        Assert.DoesNotContain("href=\"/Papers/Create\"", html);
        foreach (var route in new[] { "/MockPapers/Create", "/RealPapers/Create" })
        {
            StringAssert.Contains(html, $"href=\"{route}\"");
            using var editor = await _http.GetAsync(route);
            Assert.AreEqual(HttpStatusCode.OK, editor.StatusCode);
        }
        using var import = await _http.GetAsync("/Papers/Import");
        Assert.AreEqual(HttpStatusCode.OK, import.StatusCode, "The shared importer creates new-model papers.");
        using var cultureResponse = await _http.GetAsync("/Culture/Set?culture=zh-CN&returnUrl=/Papers");
        Assert.AreEqual(HttpStatusCode.Found, cultureResponse.StatusCode);
        var chinese = WebUtility.HtmlDecode(await _http.GetStringAsync("/Papers"));
        var resources = new System.Resources.ResourceManager("Aiursoft.WeChatExam.Resources.Views.Papers.Index", typeof(Startup).Assembly);
        var title = resources.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), true, false)!.GetString("Legacy Paper Archive");
        Assert.IsNotNull(title);
        StringAssert.Contains(chinese, title);
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

        // The old archive remains editable; only new creation has moved to the new entities.
        var paperId = await SeedLegacyPaper($"Test Paper {Guid.NewGuid()}");
        var paperEditUrl = $"/Papers/Edit/{paperId}";

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

        var paperTitle = $"Real Exam Paper {Guid.NewGuid()}";
        var paperId = (await SeedLegacyPaper(paperTitle, real: true)).ToString();
        var paperEditUrl = $"/Papers/Edit/{paperId}";

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
