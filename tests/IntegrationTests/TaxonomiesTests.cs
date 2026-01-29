using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TaxonomiesTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public TaxonomiesTests()
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
    public async Task TaxonomiesCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Taxonomy
        var taxonomyName = $"Test-Taxonomy-{Guid.NewGuid()}";
        var createToken = await GetAntiCsrfToken("/Taxonomies/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", taxonomyName },
            { "__RequestVerificationToken", createToken }
        });
        
        var createResponse = await _http.PostAsync("/Taxonomies/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var location = createResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Taxonomies/Index" || location == "/Taxonomies");
        
        // 2. Read Taxonomy (Index)
        var indexResponse = await _http.GetAsync("/Taxonomies/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains(taxonomyName, indexHtml);

        // Extract ID via Regex
        var idMatch = Regex.Match(indexHtml, $@"href=""/Taxonomies/Edit/(\d+)""");
        Assert.IsTrue(idMatch.Success);
        var taxonomyId = idMatch.Groups[1].Value;

        // 3. Edit Taxonomy
        var newName = $"Updated-Tax-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var editToken = await GetAntiCsrfToken($"/Taxonomies/Edit/{taxonomyId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", taxonomyId },
            { "Name", newName },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Taxonomies/Edit/{taxonomyId}", editContent);
        if (editResponse.StatusCode != HttpStatusCode.Found)
        {
            var html = await editResponse.Content.ReadAsStringAsync();
            Console.WriteLine(html);
        }
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync("/Taxonomies/Index");
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newName, verifyHtml);

        // 4. Delete Taxonomy
        var deleteToken = await GetAntiCsrfToken($"/Taxonomies/Delete/{taxonomyId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Taxonomies/Delete/{taxonomyId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Taxonomies/Index" || location == "/Taxonomies");

        // Verify Deletion
        var finalResponse = await _http.GetAsync("/Taxonomies/Index");
        var finalHtml = await finalResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newName, finalHtml);
    }

    [TestMethod]
    public async Task ApiTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Taxonomy via UI
        var taxonomyName = $"API-Tax-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var createToken = await GetAntiCsrfToken("/Taxonomies/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", taxonomyName },
            { "__RequestVerificationToken", createToken }
        });
        await _http.PostAsync("/Taxonomies/Create", createContent);

        // 2. Get Taxonomy List via API
        var listResponse = await _http.GetAsync("/api/miniprogramapi/taxonomies");
        listResponse.EnsureSuccessStatusCode();
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Contains(taxonomyName, listJson);

        // Get ID
        var taxonomies = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TaxonomyDto>>(listJson);
        var taxonomyId = taxonomies!.First(t => t.Name == taxonomyName).Id;

        // 3. Seed a Tag with Taxonomy
        using (var scope = _server!.Services.CreateScope())
        {
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            var tag = await tagService.AddTagAsync($"Tag-{Guid.NewGuid()}");
            tag.TaxonomyId = taxonomyId;
            await tagService.UpdateTagAsync(tag);
        }

        // 4. Get Tags by Taxonomy via API
        var tagsResponse = await _http.GetAsync($"/api/miniprogramapi/taxonomies/{taxonomyId}/tags");
        tagsResponse.EnsureSuccessStatusCode();
        var tagsJson = await tagsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Tag-", tagsJson);
    }

    [TestMethod]
    public async Task TagSearchWithTaxonomyTest()
    {
        await LoginAsAdminAsync();

        // 1. Create a Taxonomy
        var taxonomyName = $"Search-Tax-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var createToken = await GetAntiCsrfToken("/Taxonomies/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", taxonomyName },
            { "__RequestVerificationToken", createToken }
        });
        await _http.PostAsync("/Taxonomies/Create", createContent);
        
        // Get Taxonomy ID (fetch from list)
        var listResponse = await _http.GetAsync("/api/miniprogramapi/taxonomies");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var taxonomies = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TaxonomyDto>>(listJson);
        var taxonomyId = taxonomies!.First(t => t.Name == taxonomyName).Id;

        // 2. Create Tags
        string tag1Name = $"Tag-In-{Guid.NewGuid()}";
        string tag2Name = $"Tag-Out-{Guid.NewGuid()}";
        
        using (var scope = _server!.Services.CreateScope())
        {
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            
            // Tag in taxonomy
            var tag1 = await tagService.AddTagAsync(tag1Name);
            tag1.TaxonomyId = taxonomyId;
            await tagService.UpdateTagAsync(tag1);

            // Tag NOT in taxonomy
            await tagService.AddTagAsync(tag2Name);
        }

        // 3. Search Tags with Taxonomy Filter
        var searchResponse = await _http.GetAsync($"/Tags/Index?taxonomyId={taxonomyId}");
        searchResponse.EnsureSuccessStatusCode();
        var searchHtml = await searchResponse.Content.ReadAsStringAsync();

        // Verify
        Assert.Contains(tag1Name, searchHtml);
        Assert.DoesNotContain(tag2Name, searchHtml);
        
        // Note: AspNetCore TagHelper might output selected="selected" or just selected. 
        // Let's check generally for the value and selected.
        // Or cleaner: Check if the option is selected.
        var isSelected = searchHtml.Contains($"selected=\"selected\" value=\"{taxonomyId}\"");
        Assert.IsTrue(isSelected);
    }

    [TestMethod]
    public async Task UncategorizedFilterTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Tags
        string categorizedTagName = $"Tag-Cat-{Guid.NewGuid()}";
        string uncategorizedTagName = $"Tag-Uncat-{Guid.NewGuid()}";
        
        // Create a Taxonomy
        var taxonomyName = $"Uncat-Test-Tax-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var createToken = await GetAntiCsrfToken("/Taxonomies/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", taxonomyName },
            { "__RequestVerificationToken", createToken }
        });
        await _http.PostAsync("/Taxonomies/Create", createContent);
        
        // Get Taxonomy ID
        var listResponse = await _http.GetAsync("/api/miniprogramapi/taxonomies");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        var taxonomies = Newtonsoft.Json.JsonConvert.DeserializeObject<List<TaxonomyDto>>(listJson);
        var taxonomyId = taxonomies!.First(t => t.Name == taxonomyName).Id;

        using (var scope = _server!.Services.CreateScope())
        {
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            
            // Categorized Tag
            var tag1 = await tagService.AddTagAsync(categorizedTagName);
            tag1.TaxonomyId = taxonomyId;
            await tagService.UpdateTagAsync(tag1);

            // Uncategorized Tag
            await tagService.AddTagAsync(uncategorizedTagName);
        }

        // 2. Search Uncategorized Tags (taxonomyId=0)
        var searchResponse = await _http.GetAsync("/Tags/Index?taxonomyId=0");
        searchResponse.EnsureSuccessStatusCode();
        var searchHtml = await searchResponse.Content.ReadAsStringAsync();

        // Verify
        Assert.Contains(uncategorizedTagName, searchHtml);
        Assert.DoesNotContain(categorizedTagName, searchHtml);
    }
}

public class TaxonomyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
