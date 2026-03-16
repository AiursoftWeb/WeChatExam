using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class FeedbackTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService = new();
    private readonly CookieContainer _cookieContainer = new();

    public FeedbackTests()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
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
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;
        TestStartupWithMockWeChat.MockDistributionChannelService = new Mock<IDistributionChannelService>();

        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
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
        var match = Regex.Match(html, @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        return match.Groups[1].Value;
    }

    private async Task LoginAsAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        // Register
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        await _http.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        }));

        // Grant permissions
        using (var scope = _server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            await userManager.AddClaimAsync(user!, new System.Security.Claims.Claim(AppPermissions.Type, AppPermissionNames.CanReadFeedbacks));
            await userManager.AddClaimAsync(user!, new System.Security.Claims.Claim(AppPermissions.Type, AppPermissionNames.CanEditFeedbacks));
        }

        // Login again to refresh claims
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        }));
    }

    private async Task<string> GetWeChatTokenAsync(string openId)
    {
        var code = "mock-code";
        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = "mock-session-key"
            });

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = code });
        var tokenDto = await response.Content.ReadFromJsonAsync<TokenDto>();
        return tokenDto!.Token;
    }

    [TestMethod]
    public async Task FullFeedbackLifecycleTest()
    {
        // 1. MiniProgram User submits feedback
        var openId = $"user-{Guid.NewGuid()}";
        var token = await GetWeChatTokenAsync(openId);
        
        var feedbackModel = new SubmitFeedbackDto
        {
            Content = "This is a test feedback from integration test.",
            Contact = "test@integration.com",
            Type = FeedbackType.Bug
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Feedback");
        request.Headers.Add("Authorization", $"Bearer {token}");
        request.Content = JsonContent.Create(feedbackModel);
        
        var submitResponse = await _http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, submitResponse.StatusCode);

        // 2. MiniProgram User checks their feedback
        var myRequest = new HttpRequestMessage(HttpMethod.Get, "/api/Feedback/my");
        myRequest.Headers.Add("Authorization", $"Bearer {token}");
        var myResponse = await _http.SendAsync(myRequest);
        Assert.AreEqual(HttpStatusCode.OK, myResponse.StatusCode);
        var myFeedbacks = await myResponse.Content.ReadFromJsonAsync<List<FeedbackResponseDto>>();
        Assert.AreEqual(1, myFeedbacks!.Count);
        Assert.AreEqual(feedbackModel.Content, myFeedbacks[0].Content);
        Assert.AreEqual(feedbackModel.Type, myFeedbacks[0].Type);
        Assert.AreEqual(FeedbackStatus.Pending, myFeedbacks[0].Status);

        var feedbackId = myFeedbacks[0].Id;

        // 3. Admin views feedback in management panel
        await LoginAsAdminAsync();
        var indexResponse = await _http.GetAsync("/Feedbacks/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains(feedbackModel.Content, indexHtml);

        // 4. Admin processes feedback
        // Need to find the Anti-CSRF token from the index page
        var processToken = Regex.Match(indexHtml, @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />").Groups[1].Value;
        
        var processResponse = await _http.PostAsync($"/Feedbacks/Process/{feedbackId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", processToken }
        }));
        Assert.AreEqual(HttpStatusCode.Found, processResponse.StatusCode); // Redirects to Index

        // 5. User checks feedback again and sees it's processed
        var myRequest2 = new HttpRequestMessage(HttpMethod.Get, "/api/Feedback/my");
        myRequest2.Headers.Add("Authorization", $"Bearer {token}");
        var myResponse2 = await _http.SendAsync(myRequest2);
        var myFeedbacks2 = await myResponse2.Content.ReadFromJsonAsync<List<FeedbackResponseDto>>();
        Assert.AreEqual(FeedbackStatus.Processed, myFeedbacks2![0].Status);
    }

    [TestMethod]
    public async Task ManagementPanel_PermissionTest()
    {
        // Login as normal user without feedback permissions
        var email = $"user-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        await _http.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        }));

        // Try to access Feedbacks/Index
        var response = await _http.GetAsync("/Feedbacks/Index");
        
        // Should be redirected to Unauthorized or AccessDenied
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.OriginalString.Contains("Unauthorized") ?? false);
    }
}
