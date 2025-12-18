using System.Net;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// 测试认证隔离：确保微信认证和管理员认证完全独立
/// </summary>
[TestClass]
public class AuthenticationIsolationTests
{
    private WebApplicationFactory<Startup> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IWeChatService> _mockWeChatService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _mockWeChatService = new Mock<IWeChatService>();

        _factory = new WebApplicationFactory<Startup>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "AppSettings:AuthProvider", "Local" },
                        { "AppSettings:WeChatEnabled", "true" },
                        { "AppSettings:DebugMagicKey", "test-magic-key-12345" },
                        { "AppSettings:Local:AllowWeakPassword", "true" },
                        { "AppSettings:WeChat:AppId", "mock-app-id" },
                        { "AppSettings:WeChat:AppSecret", "12345678901234567890123456789012" },
                        { "ConnectionStrings:DbType", "InMemory" },
                        { "ConnectionStrings:AllowCache", "True" },
                        { "ConnectionStrings:DefaultConnection", "DataSource=:memory:" },
                        { "Logging:LogLevel:Default", "Information" },
                        { "AllowedHosts", "*" }
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove existing registration
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IWeChatService));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add mock
                    services.AddScoped(_ => _mockWeChatService.Object);
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false // 禁用自动重定向，这样我们可以检查重定向响应
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    /// <summary>
    /// 测试1：微信用户无法访问管理后台（即使有 Admin 角色）
    /// </summary>
    [TestMethod]
    public async Task WeChatUser_CannotAccess_AdminPanel_EvenWithAdminRole()
    {
        // Arrange: 获取微信用户的 debug token
        var debugTokenRequest = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        var debugTokenResponse = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", debugTokenRequest);
        Assert.AreEqual(HttpStatusCode.OK, debugTokenResponse.StatusCode);

        var tokenDto = await debugTokenResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(tokenDto);
        Assert.IsFalse(string.IsNullOrEmpty(tokenDto.Token));

        // 为这个用户添加 Admin 角色（模拟某种配置错误）
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Entities.User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();

        var debugUser = userManager.Users.FirstOrDefault(u => u.UserName == "debugger");
        Assert.IsNotNull(debugUser, "Debug user should exist");

        // 创建 Admin 角色（如果不存在）
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Admin"));
        }

        // 将 Admin 角色赋予微信用户
        await userManager.AddToRoleAsync(debugUser, "Admin");
        var roles = await userManager.GetRolesAsync(debugUser);
        Assert.IsTrue(roles.Contains("Admin"), "Debug user should have Admin role");

        // Act: 尝试使用 JWT Bearer token 访问管理后台
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokenDto.Token}");

        var adminDashboardResponse = await _client.GetAsync("/Admin/Dashboard");

        // Assert: 应该被重定向到登录页（因为 AdminOnly 要求 Cookie 认证）
        Assert.AreEqual(HttpStatusCode.Redirect, adminDashboardResponse.StatusCode);
        Assert.IsTrue(adminDashboardResponse.Headers.Location?.ToString().Contains("/Admin/Login") ?? false,
            "WeChat user with JWT token should be redirected to login page even with Admin role");
    }

    /// <summary>
    /// 测试2：管理员无法使用 Cookie 访问微信 API（除非也有 JWT token）
    /// </summary>
    [TestMethod]
    public async Task Admin_CannotAccess_WeChatAPI_WithCookieOnly()
    {
        // Arrange: 创建一个管理员用户并登录
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Entities.User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();

        // 创建管理员用户
        var adminUser = new Entities.User
        {
            UserName = "admin_test",
            DisplayName = "Admin Test User",
            Email = "admin@test.com"
        };

        await userManager.CreateAsync(adminUser, "Admin@123");

        // 创建 Admin 角色并赋予用户
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Admin"));
        }
        await userManager.AddToRoleAsync(adminUser, "Admin");

        // 通过 Web 登录获取 Cookie
        var loginModel = new AdminLoginDto
        {
            Username = "admin_test",
            Password = "Admin@123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/Admin/Login", loginModel);

        // 提取 Cookie
        if (loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                _client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            }
        }

        // Act: 尝试使用 Cookie 访问微信 API
        var wechatApiResponse = await _client.GetAsync("/api/User/info");

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

        var debugTokenResponse = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", debugTokenRequest);
        var tokenDto = await debugTokenResponse.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(tokenDto);

        // Act: 使用 JWT token 访问微信 API
        _client.DefaultRequestHeaders.Clear();
        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {tokenDto.Token}");
        var userInfoResponse = await _client.GetAsync("/api/User/info");

        // Assert: 应该成功访问
        Assert.AreEqual(HttpStatusCode.OK, userInfoResponse.StatusCode,
            "WeChat user with JWT token should be able to access WeChat API");
    }

    /// <summary>
    /// 测试4：两个认证体系完全独立 - 管理员可以访问管理后台
    /// </summary>
    [TestMethod]
    public async Task Admin_CanAccess_AdminPanel_WithCookie()
    {
        // Arrange: 创建管理员用户
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Entities.User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();

        var adminUser = new Entities.User
        {
            UserName = "admin_dashboard_test",
            DisplayName = "Admin Dashboard Test",
            Email = "admin_dashboard@test.com"
        };

        await userManager.CreateAsync(adminUser, "Admin@123");

        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            await roleManager.CreateAsync(new Microsoft.AspNetCore.Identity.IdentityRole("Admin"));
        }
        await userManager.AddToRoleAsync(adminUser, "Admin");

        // 登录获取 Cookie
        var loginModel = new AdminLoginDto
        {
            Username = "admin_dashboard_test",
            Password = "Admin@123"
        };

        var loginResponse = await _client.PostAsJsonAsync("/Admin/Login", loginModel);

        // 提取 Cookie
        if (loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var cookie in cookies)
            {
                _client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
            }
        }

        // Act: 访问管理后台
        var dashboardResponse = await _client.GetAsync("/Admin/Dashboard");

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
        var response = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);

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
        var response = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);

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
        var response1 = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);
        Assert.AreEqual(HttpStatusCode.OK, response1.StatusCode);
        var token1 = await response1.Content.ReadFromJsonAsync<TokenDto>();

        // Act: 第二次调用，应该复用已有用户
        var response2 = await _client.PostAsJsonAsync("/api/Auth/exchange_debug_token", request);
        Assert.AreEqual(HttpStatusCode.OK, response2.StatusCode);
        var token2 = await response2.Content.ReadFromJsonAsync<TokenDto>();

        // Assert: OpenId 应该相同（表示使用了同一个用户）
        Assert.AreEqual(token1!.OpenId, token2!.OpenId);

        // 验证数据库中只有一个 debugger 用户
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Entities.User>>();
        var debugUsers = userManager.Users.Where(u => u.UserName == "debugger").ToList();
        Assert.AreEqual(1, debugUsers.Count, "Should have exactly one debugger user");
    }
}
