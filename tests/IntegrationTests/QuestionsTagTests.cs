using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionsTagTests
{
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
    }

    [TestMethod]
    public async Task CreateAndEditQuestionWithTagsTest()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();

        // 2. Create Question with Tags (Chinese)
        var qText = $"Test Question {Guid.NewGuid()}";
        var tags = "贝多芬 钢琴 奏鸣曲"; 
        
        var createToken = await GetAntiCsrfToken($"/Questions/Create?questionCategoryId={periodId}");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" }, // Choice
            { "GradingStrategy", "0" }, // AllOrNothing
            { "QuestionCategoryId", periodId.ToString() },
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
        Assert.Contains("content-editor.js", editHtml);
        Assert.Contains("name=\"Tags\"", editHtml);
        foreach (var tag in tags.Split(' '))
            Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(tag), editHtml);
    }

    [TestMethod]
    public async Task CreatePageKeepsTagsAfterValidation()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Draft tag category", ParentId = null };
        db.Categories.Add(category); await db.SaveChangesAsync();
        var text = $"Draft tagged question {Guid.NewGuid()}";
        var tags = "伯牙 琴曲";
        var form = new Dictionary<string, string>
        {
            ["Content"] = text, ["QuestionType"] = "0", ["GradingStrategy"] = "0",
            ["Options[0]"] = "A", ["Options[1]"] = "B", ["StandardAnswer"] = "A",
            ["QuestionCategoryId"] = "", ["Tags"] = tags,
            ["__RequestVerificationToken"] = await GetAntiCsrfToken("/Questions/Create")
        };
        var invalid = await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(form));
        invalid.EnsureSuccessStatusCode();
        var html = await invalid.Content.ReadAsStringAsync();
        var hiddenTags = Regex.Match(html, @"<input\b(?=[^>]*\bname=""Tags"")[^>]*\bvalue=""([^""]*)""");
        Assert.IsTrue(hiddenTags.Success);
        Assert.AreEqual(tags, WebUtility.HtmlDecode(hiddenTags.Groups[1].Value));
        Assert.Contains("content-editor.js", html);
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Content == text));
        form["QuestionCategoryId"] = periodId.ToString();
        form["__RequestVerificationToken"] = await GetAntiCsrfToken("/Questions/Create");
        var saved = await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(form));
        Assert.AreEqual(HttpStatusCode.Found, saved.StatusCode);
        var question = await db.Questions.SingleAsync(q => q.Content == text);
        var actualTags = await db.QuestionTags.Where(q => q.QuestionId == question.Id)
            .Select(q => q.Tag.DisplayName).ToArrayAsync();
        CollectionAssert.AreEquivalent(tags.Split(' '), actualTags);
    }
}
