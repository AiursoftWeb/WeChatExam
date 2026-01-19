using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using static Aiursoft.WebTools.Extends;

[assembly: DoNotParallelize]

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// 测试微信小程序认证：登录、token使用、用户资料更新等功能
/// </summary>
[TestClass]
public class WeChatAuthTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService = new();

    public WeChatAuthTests()
    {
        var handler = new HttpClientHandler
        {
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

    [TestMethod]
    public async Task Login_ValidCode_ReturnsToken()
    {
        // Arrange: 设置mock服务，模拟微信登录成功
        var code = "valid-code";
        var openId = "mock-openid";
        var sessionKey = "mock-session-key";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });

        var model = new Code2SessionDto { Code = code };

        // Act: 调用登录接口
        var response = await _http.PostAsJsonAsync("/api/Auth/login", model);

        // Assert: 验证返回的token
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(token);
        Assert.AreEqual(openId, token.OpenId);
        Assert.IsFalse(string.IsNullOrEmpty(token.Token));
        Assert.IsTrue(token.Expiration > DateTime.UtcNow);
    }

    [TestMethod]
    public async Task Login_InvalidCode_ReturnsBadRequest()
    {
        // Arrange: 设置mock服务，模拟微信code无效
        var code = "invalid-code";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = false,
                ErrorCode = 40029,
                ErrorMessage = "Invalid code"
            });

        var model = new Code2SessionDto { Code = code };

        // Act: 调用登录接口
        var response = await _http.PostAsJsonAsync("/api/Auth/login", model);

        // Assert: 验证返回400错误
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid code", content);
    }

    [TestMethod]
    public async Task Login_EmptyCode_ReturnsBadRequest()
    {
        // Arrange: 使用空code
        var model = new Code2SessionDto { Code = "" };

        // Act: 调用登录接口
        var response = await _http.PostAsJsonAsync("/api/Auth/login", model);

        // Assert: 验证返回400错误
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Code", content); // 验证错误消息包含Code字段
    }


    [TestMethod]
    public async Task Login_FirstTime_CreatesUser()
    {
        // Arrange: 设置mock服务
        var code = "first-time-code";
        var openId = $"new-openid-{Guid.NewGuid()}";
        var sessionKey = "new-session-key";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });


        var model = new Code2SessionDto { Code = code };

        // Act: 首次登录
        var response = await _http.PostAsJsonAsync("/api/Auth/login", model);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Assert: 验证用户已创建
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = userManager.Users.FirstOrDefault(u => u.MiniProgramOpenId == openId);
        Assert.IsNotNull(user, "User should be created on first login");
        Assert.AreEqual(openId, user.MiniProgramOpenId);
        Assert.AreEqual(sessionKey, user.SessionKey);
    }

    [TestMethod]
    public async Task Login_SecondTime_UpdatesSessionKey()
    {
        // Arrange: 首次登录
        var code = "first-code";
        var openId = $"existing-openid-{Guid.NewGuid()}";
        var firstSessionKey = "first-session-key";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = firstSessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });

        var model = new Code2SessionDto { Code = code };
        await _http.PostAsJsonAsync("/api/Auth/login", model);

        // Act: 第二次登录，使用不同的sessionKey
        var secondCode = "second-code";
        var secondSessionKey = "second-session-key";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(secondCode))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = secondSessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });

        var secondModel = new Code2SessionDto { Code = secondCode };
        var secondResponse = await _http.PostAsJsonAsync("/api/Auth/login", secondModel);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);

        // Assert: 验证sessionKey已更新
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = userManager.Users.FirstOrDefault(u => u.MiniProgramOpenId == openId);
        Assert.IsNotNull(user);
        Assert.AreEqual(secondSessionKey, user.SessionKey, "SessionKey should be updated on second login");

        // 验证只有一个用户（没有重复创建）
        var userCount = userManager.Users.Count(u => u.MiniProgramOpenId == openId);
        Assert.AreEqual(1, userCount, "Should have exactly one user with this OpenId");
    }

    [TestMethod]
    public async Task Login_UseToken_CanAccessApi()
    {
        // Arrange: 登录并获取token
        var code = "valid-code-for-api";
        var openId = "mock-openid-api";
        var sessionKey = "mock-session-key-api";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });

        var model = new Code2SessionDto { Code = code };
        var loginResponse = await _http.PostAsJsonAsync("/api/Auth/login", model);
        var tokenDto = await loginResponse.Content.ReadFromJsonAsync<TokenDto>();
        var token = tokenDto!.Token;

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act: 使用token访问API
        var response = await _http.GetAsync("/api/User/info");

        // Assert: 验证可以成功访问
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var userInfo = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.IsNotNull(userInfo);
    }

    [TestMethod]
    public async Task AccessApi_WithoutToken_ReturnsUnauthorized()
    {
        // Act: 不使用token访问API
        var response = await _http.GetAsync("/api/User/info");

        // Assert: 验证返回401
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AccessApi_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange: 使用一个伪造的token
        _http.DefaultRequestHeaders.Add("Authorization", "Bearer invalid-fake-token-12345");

        // Act: 尝试访问API
        var response = await _http.GetAsync("/api/User/info");

        // Assert: 验证返回401
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UpdateProfile_WithValidToken_UpdatesUserInfo()
    {
        // Arrange: 登录并获取token
        var code = "code-for-profile-update";
        var openId = $"openid-for-profile-{Guid.NewGuid()}";
        var sessionKey = "session-key";

        _mockWeChatService
            .Setup(s => s.CodeToSessionAsync(code))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey,
                ErrorCode = 0,
                ErrorMessage = null
            });

        var loginModel = new Code2SessionDto { Code = code };
        var loginResponse = await _http.PostAsJsonAsync("/api/Auth/login", loginModel);
        var tokenDto = await loginResponse.Content.ReadFromJsonAsync<TokenDto>();
        var token = tokenDto!.Token;

        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act: 更新用户资料
        var updateModel = new UpdateProfileDto
        {
            NickName = "测试昵称",
            AvatarUrl = "https://example.com/avatar.jpg"
        };
        var updateResponse = await _http.PostAsJsonAsync("/api/User/profile", updateModel);


        // Assert: 验证更新成功
        Assert.AreEqual(HttpStatusCode.OK, updateResponse.StatusCode);

        // 验证数据库中的用户信息已更新
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = userManager.Users.FirstOrDefault(u => u.MiniProgramOpenId == openId);
        Assert.IsNotNull(user);
        Assert.AreEqual("测试昵称", user.DisplayName);
        Assert.AreEqual("https://example.com/avatar.jpg", user.AvatarRelativePath);
    }
}
