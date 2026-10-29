using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TaxonomiesTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService = new();
    private readonly StubWeChatPayService _mockWeChatPayService = new();
    private readonly StubDistributionChannelService _mockDistributionChannelService = new();

    public TaxonomiesTests()
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
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;
        TestStartupWithMockWeChat.MockWeChatPayService = _mockWeChatPayService;
        TestStartupWithMockWeChat.MockDistributionChannelService = _mockDistributionChannelService;

        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: reservation.Port);
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
    private async Task<string> LoginAsWeChatUserAsync()
    {
        _mockWeChatService.DefaultSession = new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = $"test-openid-{Guid.NewGuid()}",
                SessionKey = "test-session-key"
            };

        _mockWeChatPayService.Memberships = new List<VipMembership>();

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto
        {
            Code = "test-code"
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
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
    public async Task InternalTaxonomyEditingPreservesTagsWithoutLegacyCourseSelection()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ITaxonomyService>();
        var taxonomy = await service.AddTaxonomyAsync("Internal sources");
        Assert.AreEqual(taxonomy.Id, (await service.AddTaxonomyAsync("Internal sources")).Id);
        var tag = new Tag { DisplayName = "Source year", NormalizedName = "SOURCE YEAR", TaxonomyId = taxonomy.Id };
        var question = new Question { Content = "Preserved question", QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true) };
        db.AddRange(tag, question);
        await db.SaveChangesAsync();
        db.Add(new QuestionTag { TagId = tag.Id, QuestionId = question.Id });
        await db.SaveChangesAsync();
        Assert.DoesNotContain("name=\"CategoryId\"", await _http.GetStringAsync("/Taxonomies/Create"));
        var url = $"/Taxonomies/Edit/{taxonomy.Id}";
        Assert.DoesNotContain("name=\"CategoryId\"", await _http.GetStringAsync(url));
        var invalid = await _http.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = taxonomy.Id.ToString(), ["Name"] = "", ["checkReturnUrl"] = "/ContentChecks/Results?view=all",
            ["__RequestVerificationToken"] = await GetAntiCsrfToken(url)
        }));
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.DoesNotContain("name=\"CategoryId\"", invalidHtml);
        Assert.Contains("/ContentChecks/Results?view=all", invalidHtml);
        var edited = await _http.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = taxonomy.Id.ToString(), ["Name"] = "Edited sources", ["CategoryId"] = Guid.NewGuid().ToString(),
            ["checkReturnUrl"] = "/ContentChecks/Results?view=all",
            ["__RequestVerificationToken"] = await GetAntiCsrfToken(url)
        }));
        Assert.AreEqual(HttpStatusCode.Found, edited.StatusCode);
        Assert.AreEqual("/ContentChecks/Results?view=all", edited.Headers.Location!.OriginalString);
        db.ChangeTracker.Clear();
        Assert.AreEqual("Edited sources", (await db.Taxonomies.FindAsync(taxonomy.Id))!.Name);
        var tagPage = await _http.GetStringAsync($"/Tags/Index?taxonomyId={taxonomy.Id}");
        Assert.Contains("Source year", tagPage);
        Assert.Contains("Edited sources", tagPage);
        Assert.AreEqual(taxonomy.Id, (await db.Tags.FindAsync(tag.Id))!.TaxonomyId);
        await service.DeleteTaxonomyAsync(taxonomy.Id);
        db.ChangeTracker.Clear();
        Assert.IsNull((await db.Tags.FindAsync(tag.Id))!.TaxonomyId);
        Assert.IsTrue(await db.QuestionTags.AnyAsync(t => t.TagId == tag.Id && t.QuestionId == question.Id));
        Assert.IsTrue(await db.Questions.AnyAsync(q => q.Id == question.Id));
    }

    [TestMethod]
    public async Task TagEditorOnlyManagesInternalMetadataAndKeepsCheckReturn()
    {
        await LoginAsAdminAsync();
        var name = "Internal tag " + Guid.NewGuid().ToString("N")[..8];
        var create = await _http.GetStringAsync("/Tags/Create");
        Assert.DoesNotContain("name=\"IsFree\"", create);
        Assert.DoesNotContain("VIP membership", create);
        var created = await _http.PostAsync("/Tags/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["DisplayName"] = name, ["IsFree"] = "false",
            ["__RequestVerificationToken"] = await GetAntiCsrfToken("/Tags/Create")
        }));
        Assert.AreEqual(HttpStatusCode.Found, created.StatusCode);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var tag = await db.Tags.SingleAsync(t => t.DisplayName == name);
        var editUrl = $"/Tags/Edit/{tag.Id}";
        var edit = await _http.GetStringAsync(editUrl);
        Assert.DoesNotContain("name=\"IsFree\"", edit);
        Assert.DoesNotContain("VIP membership", edit);
        var response = await _http.PostAsync(editUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = tag.Id.ToString(), ["DisplayName"] = name + " edited", ["IsFree"] = "true",
            ["checkReturnUrl"] = "/ContentChecks/Results?view=all",
            ["__RequestVerificationToken"] = await GetAntiCsrfToken(editUrl)
        }));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.AreEqual("/ContentChecks/Results?view=all", response.Headers.Location!.OriginalString);
        db.ChangeTracker.Clear();
        Assert.AreEqual(name + " edited", (await db.Tags.FindAsync(tag.Id))!.DisplayName);
        var listing = await _http.GetStringAsync("/Tags/Index?search=" + Uri.EscapeDataString(name));
        Assert.Contains(name + " edited", listing);
        Assert.DoesNotContain("VIP Required", listing);
        Assert.DoesNotContain("data-lucide=\"crown\"", listing);
    }

    [TestMethod]
    public async Task StudentNavigationIsRetiredWithoutRemovingInternalTags()
    {
        await LoginAsAdminAsync();
        var name = $"Internal-{Guid.NewGuid()}";
        var createToken = await GetAntiCsrfToken("/Taxonomies/Create");
        var created = await _http.PostAsync("/Taxonomies/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", name }, { "__RequestVerificationToken", createToken }
        }));
        Assert.AreEqual(HttpStatusCode.Found, created.StatusCode);
        int taxonomyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            taxonomyId = db.Taxonomies.Single(t => t.Name == name).Id;
            db.Tags.Add(new Tag { DisplayName = name, NormalizedName = name.ToUpperInvariant(), TaxonomyId = taxonomyId });
            await db.SaveChangesAsync();
        }

        // An administrator's cookie or a student's JWT cannot revive retired navigation.
        foreach (var jwt in new[] { false, true })
        {
            if (jwt) _http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsWeChatUserAsync());
            Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync("/api/miniprogramapi/taxonomies")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/miniprogramapi/taxonomies/{taxonomyId}/tags")).StatusCode);
        }
        var tags = await _http.GetAsync($"/Tags/Index?taxonomyId={taxonomyId}");
        tags.EnsureSuccessStatusCode();
        Assert.Contains(name, await tags.Content.ReadAsStringAsync());
        using var verifyScope = _server!.Services.CreateScope();
        var context = verifyScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(1, context.Tags.Count(t => t.TaxonomyId == taxonomyId));
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
        
        // Read the ID created by the management form, without using student navigation.
        int taxonomyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            taxonomyId = context.Taxonomies.Single(t => t.Name == taxonomyName).Id;
        }

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
        int taxonomyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            taxonomyId = context.Taxonomies.Single(t => t.Name == taxonomyName).Id;
        }

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
