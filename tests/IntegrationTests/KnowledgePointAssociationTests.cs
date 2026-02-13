using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class KnowledgePointAssociationTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public KnowledgePointAssociationTests()
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
    public async Task TestKnowledgePointCategoryAssociation()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = "Test Association Category";
        var createCatToken = await GetAntiCsrfToken("/Categories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/Categories/Create", createCatContent);
        Assert.AreEqual(HttpStatusCode.Found, createCatResponse.StatusCode);
        var categoryId = createCatResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create a Knowledge Point
        var kpTitle = "Test Association KP";
        var kpContent = "Test KP Content";
        var createKpToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", kpTitle },
            { "Content", kpContent },
            { "__RequestVerificationToken", createKpToken }
        });
        var createKpResponse = await _http.PostAsync("/KnowledgePoints/Create", createKpContent);
        Assert.AreEqual(HttpStatusCode.Found, createKpResponse.StatusCode);
        var kpId = createKpResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Associate them via Knowledge Point Edit
        var editKpToken = await GetAntiCsrfToken($"/KnowledgePoints/Edit/{kpId}");
        var editKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", kpId },
            { "Title", kpTitle },
            { "Content", kpContent },
            { "SelectedCategoryIds[0]", categoryId },
            { "__RequestVerificationToken", editKpToken }
        });
        var editKpResponse = await _http.PostAsync($"/KnowledgePoints/Edit/{kpId}", editKpContent);
        Assert.AreEqual(HttpStatusCode.Found, editKpResponse.StatusCode);

        // 4. Verify Association in Knowledge Point Details
        var kpDetailsResponse = await _http.GetAsync($"/KnowledgePoints/Details/{kpId}");
        kpDetailsResponse.EnsureSuccessStatusCode();
        var kpDetailsHtml = await kpDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, kpDetailsHtml, "Knowledge Point details should show associated category");

        // 5. Verify Association in Category Details
        var catDetailsResponse = await _http.GetAsync($"/Categories/Details/{categoryId}");
        catDetailsResponse.EnsureSuccessStatusCode();
        var catDetailsHtml = await catDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(kpTitle, catDetailsHtml, "Category details should show associated knowledge point");
    }

    [TestMethod]
    public async Task TestKnowledgePointCategoryAssociationAtCreation()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = "Created Category";
        var createCatToken = await GetAntiCsrfToken("/Categories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/Categories/Create", createCatContent);
        Assert.AreEqual(HttpStatusCode.Found, createCatResponse.StatusCode);
        var categoryId = createCatResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create a Knowledge Point with the category
        var kpTitle = "KP with Category";
        var kpContent = "KP Content";
        var createKpToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", kpTitle },
            { "Content", kpContent },
            { "SelectedCategoryIds[0]", categoryId },
            { "__RequestVerificationToken", createKpToken }
        });
        var createKpResponse = await _http.PostAsync("/KnowledgePoints/Create", createKpContent);
        Assert.AreEqual(HttpStatusCode.Found, createKpResponse.StatusCode);
        var kpId = createKpResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Verify Association in Knowledge Point Details
        var kpDetailsResponse = await _http.GetAsync($"/KnowledgePoints/Details/{kpId}");
        kpDetailsResponse.EnsureSuccessStatusCode();
        var kpDetailsHtml = await kpDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, kpDetailsHtml, "Knowledge Point details should show associated category after creation");

        // 4. Verify Association in Category Details
        var catDetailsResponse = await _http.GetAsync($"/Categories/Details/{categoryId}");
        catDetailsResponse.EnsureSuccessStatusCode();
        var catDetailsHtml = await catDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(kpTitle, catDetailsHtml, "Category details should show associated knowledge point after creation");
    }
}
