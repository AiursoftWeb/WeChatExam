using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TagsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TagsBatchTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public TagsBatchTests()
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
    public async Task BatchDeleteTagsTest()
    {
        await LoginAsAdminAsync();

        // 1. Create multiple tags
        int tagId1, tagId2;
        using (var scope = _server!.Services.CreateScope())
        {
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();
            var tag1 = await tagService.AddTagAsync($"BatchDelete-1-{Guid.NewGuid()}");
            var tag2 = await tagService.AddTagAsync($"BatchDelete-2-{Guid.NewGuid()}");
            tagId1 = tag1.Id;
            tagId2 = tag2.Id;
        }

        // 2. Get Anti-CSRF token for the batch delete request (can get from Tags/Index)
        var token = await GetAntiCsrfToken("/Tags/Index");

        // 3. Send BatchDelete request
        var request = new BatchDeleteRequest
        {
            TagIds = new[] { tagId1, tagId2 }
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/Tags/BatchDelete")
        {
            Content = JsonContent.Create(request)
        };
        httpRequest.Headers.Add("RequestVerificationToken", token);

        var response = await _http.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BatchDeleteResult>();
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.DeletedCount);
        CollectionAssert.AreEquivalent(new[] { tagId1, tagId2 }, result.DeletedIds);

        // 4. Verify tags are deleted in DB
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var remainingTagsCount = await context.Tags
                .Where(t => t.Id == tagId1 || t.Id == tagId2)
                .CountAsync();
            Assert.AreEqual(0, remainingTagsCount);
        }
    }
}
