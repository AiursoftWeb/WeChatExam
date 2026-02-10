using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CategoryHierarchyTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public CategoryHierarchyTests()
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
    public async Task TestSubCategoryAsParent()
    {
        await LoginAsAdminAsync();

        // 1. Create Root Category
        var rootTitle = "Root Category";
        var createToken1 = await GetAntiCsrfToken("/Categories/Create");
        var createContent1 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", rootTitle },
            { "__RequestVerificationToken", createToken1 }
        });
        var createResponse1 = await _http.PostAsync("/Categories/Create", createContent1);
        Assert.AreEqual(HttpStatusCode.Found, createResponse1.StatusCode);
        var rootId = createResponse1.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create Level 1 Category (Child of Root)
        var level1Title = "Level 1 Category";
        var createToken2 = await GetAntiCsrfToken("/Categories/Create");
        var createContent2 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", level1Title },
            { "ParentId", rootId },
            { "__RequestVerificationToken", createToken2 }
        });
        var createResponse2 = await _http.PostAsync("/Categories/Create", createContent2);
        Assert.AreEqual(HttpStatusCode.Found, createResponse2.StatusCode);
        var level1Id = createResponse2.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Check if Level 1 Category is available as parent in Create page
        var createPageResponse = await _http.GetAsync("/Categories/Create");
        createPageResponse.EnsureSuccessStatusCode();
        var createPageHtml = await createPageResponse.Content.ReadAsStringAsync();
        
        // Before fix, Level 1 Category would NOT be in AvailableParents because it's not a root category.
        // Now it SHOULD be there.
        Assert.IsTrue(createPageHtml.Contains(level1Id), "Level 1 category should be available as a parent in Create page");
        Assert.IsTrue(createPageHtml.Contains(level1Title), "Level 1 category title should be visible in Create page");

        // 4. Create Level 2 Category (Child of Level 1)
        var level2Title = "Level 2 Category";
        var createToken3 = await GetAntiCsrfToken("/Categories/Create");
        var createContent3 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", level2Title },
            { "ParentId", level1Id },
            { "__RequestVerificationToken", createToken3 }
        });
        var createResponse3 = await _http.PostAsync("/Categories/Create", createContent3);
        Assert.AreEqual(HttpStatusCode.Found, createResponse3.StatusCode);
        var level2Id = createResponse3.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 5. Verify Hierarchy in details
        var detailsResponse = await _http.GetAsync($"/Categories/Details/{level2Id}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(level2Title, detailsHtml);
        Assert.Contains(level1Title, detailsHtml); // Should show its parent title
    }
}
