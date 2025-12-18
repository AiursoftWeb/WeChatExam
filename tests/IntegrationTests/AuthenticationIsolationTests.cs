using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Identity;
using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// 测试认证隔离：确保微信认证和管理员认证完全独立
/// </summary>
[TestClass]
public class AuthenticationIsolationTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService = new();

    public AuthenticationIsolationTests()
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
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;

        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.UpdateDbAsync<TemplateDbContext>();
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

    /// <summary>
    /// 测试1：微信用户无法访问管理后台（即使有 Admin 角色）
    /// </summary>
    [TestMethod]
    public async Task WeChatUser_CannotAccess_WebPanel_EvenWithAdminRole()
    {
        // Arrange: 获取微信用户的 debug token
        var debugTokenRequest = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        var debugTokenResponse = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", debugTokenRequest);
        Assert.AreEqual(HttpStatusCode.OK, debugTokenResponse.StatusCode);

        var tokenDto = await debugTokenResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(tokenDto);
        Assert.IsFalse(string.IsNullOrEmpty(tokenDto.Token));

        // 为这个用户添加 Admin 角色（模拟某种配置错误）
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var debugUser = userManager.Users.FirstOrDefault(u => u.UserName == "debugger");
        Assert.IsNotNull(debugUser, "Debug user should exist");

        // 创建 Admin 角色（如果不存在）
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }

        // 将 Admin 角色赋予微信用户
        await userManager.AddToRoleAsync(debugUser, "Admin");
        var roles = await userManager.GetRolesAsync(debugUser);
        Assert.Contains("Admin", roles, "Debug user should have Admin role");

        // Act: 尝试使用 JWT Bearer token 访问管理后台
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokenDto.Token}");

        var adminDashboardResponse = await _http.GetAsync("/Manage/Index");

        // Assert: 应该被重定向到登录页（因为 ManageController 要求 Cookie 认证）
        Assert.AreEqual(HttpStatusCode.Redirect, adminDashboardResponse.StatusCode);
        Assert.IsTrue(adminDashboardResponse.Headers.Location?.ToString().Contains("/Account/Login") ?? false,
            "WeChat user with JWT token should be redirected to login page even with Admin role");
    }

    /// <summary>
    /// 测试2：管理员无法使用 Cookie 访问微信 API（除非也有 JWT token）
    /// </summary>
    [TestMethod]
    public async Task Admin_CannotAccess_WeChatAPI_WithCookieOnly()
    {
        // Arrange: 创建一个管理员用户并登录
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        // 创建管理员用户
        var adminUser = new User
        {
            UserName = "admin_test",
            DisplayName = "Admin Test User",
            Email = "admin@test.com"
        };

        var result = await userManager.CreateAsync(adminUser, "Admin@123");
        Assert.IsTrue(result.Succeeded);

        // 创建 Admin 角色并赋予用户
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
        await userManager.AddToRoleAsync(adminUser, "Admin");

        // 通过 Web 登录获取 Cookie
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", "admin_test" },
            { "Password", "Admin@123" },
            { "__RequestVerificationToken", loginToken }
        });

        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode, "Admin login failed");
        // HttpClient会自动处理CookieContainer

        // Act: 尝试使用 Cookie 访问微信 API
        var wechatApiResponse = await _http.GetAsync("/api/User/info");

        // Assert: 应该返回 401 Unauthorized（因为 WeChatUserOnly 要求 JWT Bearer）
        Assert.AreEqual(HttpStatusCode.Unauthorized, wechatApiResponse.StatusCode,
            "Admin with Cookie should not be able to access WeChat API without JWT token");
    }

    /// <summary>
    /// 测试3：两个认证体系完全独立 - 微信用户可以访问微信API
    /// </summary>
    [TestMethod]
    public async Task WeChatUser_CanAccess_WeChatAPI_WithJwtToken()
    {
        // Arrange: 获取微信用户的 debug token
        var debugTokenRequest = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        var debugTokenResponse = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", debugTokenRequest);
        var tokenDto = await debugTokenResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(tokenDto);

        // Act: 使用 JWT token 访问微信 API
        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokenDto.Token}");
        var userInfoResponse = await _http.GetAsync("/api/User/info");

        // Assert: 应该成功访问
        Assert.AreEqual(HttpStatusCode.OK, userInfoResponse.StatusCode,
            "WeChat user with JWT token should be able to access WeChat API");
    }

    /// <summary>
    /// 测试4：两个认证体系完全独立 - 管理员可以访问管理后台
    /// </summary>
    [TestMethod]
    public async Task Admin_CanAccess_WebPanel_WithCookie()
    {
        // Arrange: 创建管理员用户
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var adminUser = new User
        {
            UserName = "admin_dashboard_test",
            DisplayName = "Admin Dashboard Test",
            Email = "admin_dashboard@test.com"
        };

        var result = await userManager.CreateAsync(adminUser, "Admin@123");
        Assert.IsTrue(result.Succeeded);

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new IdentityRole("Admin"));
        }
        await userManager.AddToRoleAsync(adminUser, "Admin");

        // 登录获取 Cookie
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", "admin_dashboard_test" },
            { "Password", "Admin@123" },
            { "__RequestVerificationToken", loginToken }
        });

        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Redirect, loginResponse.StatusCode, "Admin login failed");

        // Act: 访问管理后台
        var dashboardResponse = await _http.GetAsync("/Manage/Index");

        // Assert: 应该成功访问
        Assert.AreEqual(HttpStatusCode.OK, dashboardResponse.StatusCode,
            "Admin with Cookie and Admin role should be able to access admin panel");
    }

    /// <summary>
    /// 测试5：Debug Token Exchange API 正常工作
    /// </summary>
    [TestMethod]
    public async Task DebugTokenExchange_Works_WithValidMagicKey()
    {
        // Arrange
        var request = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        // Act
        var response = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var tokenDto = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(tokenDto);
        Assert.IsFalse(string.IsNullOrEmpty(tokenDto.Token));
        Assert.IsFalse(string.IsNullOrEmpty(tokenDto.OpenId));
        Assert.IsTrue(tokenDto.Expiration > DateTime.UtcNow);
    }

    /// <summary>
    /// 测试6：Debug Token Exchange 在无效 magic key 时失败
    /// </summary>
    [TestMethod]
    public async Task DebugTokenExchange_Fails_WithInvalidMagicKey()
    {
        // Arrange
        var request = new DebugTokenRequestDto
        {
            MagicKey = "invalid-magic-key"
        };

        // Act
        var response = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);

        // Assert
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 测试7：验证 debugger 用户创建和复用
    /// </summary>
    [TestMethod]
    public async Task DebugTokenExchange_CreatesAndReusesDebuggerUser()
    {
        // Arrange
        var request = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        // Act: 第一次调用，应该创建用户
        var response1 = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);
        Assert.AreEqual(HttpStatusCode.OK, response1.StatusCode);
        var token1 = await response1.Content.ReadFromJsonAsync<TokenDto>();

        // Act: 第二次调用，应该复用已有用户
        var response2 = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);
        Assert.AreEqual(HttpStatusCode.OK, response2.StatusCode);
        var token2 = await response2.Content.ReadFromJsonAsync<TokenDto>();

        // Assert: OpenId 应该相同（表示使用了同一个用户）
        Assert.AreEqual(token1!.OpenId, token2!.OpenId);

        // 验证数据库中只有一个 debugger 用户
        using var scope = _server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var debugUsers = userManager.Users.Where(u => u.UserName == "debugger").ToList();
        Assert.HasCount(1, debugUsers, "Should have exactly one debugger user");
    }
}
