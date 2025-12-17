using System.Net;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;

[assembly: DoNotParallelize]

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class WeChatAuthTests
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
                        { "AppSettings:WechatAppId", "mock-app-id" },
                        { "AppSettings:WechatAppSecret", "12345678901234567890123456789012" },
                        { "ConnectionStrings:DbType", "InMemory" },
                        { "ConnectionStrings:AllowCache", "True" },
                        { "ConnectionStrings:DefaultConnection", "DataSource=:memory:" }, // Ignored by InMemory but good to have
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

                    services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
                        var secret = config["AppSettings:WechatAppSecret"];
                        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret!));
                        options.TokenValidationParameters.IssuerSigningKey = key;
                        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Login_ValidCode_ReturnsToken()
    {
        // Arrange
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

        // Act
        var response = await _client.PostAsJsonAsync("/api/Auth/login", model);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(token);
        Assert.AreEqual(openId, token.OpenId);
        Assert.IsFalse(string.IsNullOrEmpty(token.Token));
    }

    [TestMethod]
    public async Task Login_InvalidCode_ReturnsBadRequest()
    {
        // Arrange
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

        // Act
        var response = await _client.PostAsJsonAsync("/api/Auth/login", model);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_UseToken_CanAccessApi()
    {
        // Arrange
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
        var loginResponse = await _client.PostAsJsonAsync("/api/Auth/login", model);
        var tokenDto = await loginResponse.Content.ReadFromJsonAsync<TokenDto>();
        var token = tokenDto!.Token;

        _client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        // Act
        var response = await _client.GetAsync("/api/User/info");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var userInfo = await response.Content.ReadFromJsonAsync<dynamic>();
        Assert.IsNotNull(userInfo);
    }

    [TestMethod]
    public async Task AccessApi_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/User/info");

        // Assert
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
