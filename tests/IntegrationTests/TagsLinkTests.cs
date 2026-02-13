using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TagsLinkTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public TagsLinkTests()
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
    public async Task TagsAreLinksToQuestionsIndex()
    {
        await LoginAsAdminAsync();

        // 1. Create a Tag
        var tagName = $"LinkTestTag-{Guid.NewGuid()}";
        using (var scope = _server!.Services.CreateScope())
        {
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            await tagService.AddTagAsync(tagName);
        }

        // 2. Visit Tags Index
        var response = await _http.GetAsync("/Tags/Index");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // 3. Verify that the tag is a link to Questions/Index with the correct query parameter
        // Expected: <a href="/Questions?tag=tagName" ...>tagName...</a>
        // Note: asp-route-tag generates query string.
        
        // Regex to match the anchor tag
        // We look for href containing /Questions and tag=tagName
        // Since attributes order can vary, we just check if an anchor exists with the correct href.
        
        // The href might be encoded.
        var expectedUrlPart = $"/Questions?tag={WebUtility.UrlEncode(tagName)}";
        
        // Simplest check: does the HTML contain the href?
        // Note: AspNetCore might optimize the URL or add other params if defaults are involved?
        // The View code: <a asp-controller="Questions" asp-action="Index" asp-route-tag="@tag.DisplayName" ...
        // Generated: href="/Questions?tag=..."
        
        // We need to be careful about HTML encoding in the href.
        // tagName is safe here (GUID based).
        
        Assert.IsTrue(html.Contains(expectedUrlPart), $"HTML should contain link to '{expectedUrlPart}'.");
        
        // Also check that it's an anchor tag around the text (simplistic check)
        // We can check if the tag name is present.
        Assert.IsTrue(html.Contains(tagName));
    }
}
