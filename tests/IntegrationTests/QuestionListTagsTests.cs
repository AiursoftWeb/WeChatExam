using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using static Aiursoft.WebTools.Extends;
using System.Text.RegularExpressions;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionListTagsTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public QuestionListTagsTests()
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
    public async Task QuestionListDisplaysTags()
    {
        await LoginAsAdminAsync();

        string tagName = $"TestTag-{Guid.NewGuid()}";
        string questionContent = $"Question with tags {Guid.NewGuid()}";

        using (var scope = _server!.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            
            var category = new Category { Title = "Test Category", ParentId = null };
            dbContext.Categories.Add(category);
            await dbContext.SaveChangesAsync();

            var question = new Question
            {
                Content = questionContent,
                QuestionType = QuestionType.Choice,
                GradingStrategy = GradingStrategy.ExactMatch,
                CategoryId = category.Id
            };
            dbContext.Questions.Add(question);
            await dbContext.SaveChangesAsync();

            var tag = await tagService.AddTagAsync(tagName);
            await tagService.AddTagToQuestionAsync(question.Id, tag.Id);
        }

        // Visit Question Index
        var response = await _http.GetAsync("/Questions/Index");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Verify that the tag is displayed and links to the filtered index
        Assert.IsTrue(html.Contains(questionContent), "Question content should be present");
        Assert.IsTrue(html.Contains(tagName), "Tag name should be present");
        
        var expectedLink = $"/Questions?tag={WebUtility.UrlEncode(tagName)}";
        Assert.IsTrue(html.Contains(expectedLink), $"HTML should contain link to '{expectedLink}'");
    }
}
