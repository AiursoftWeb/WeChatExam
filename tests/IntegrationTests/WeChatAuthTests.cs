using System.Net;
using System.Net.Http.Json;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Senparc.Weixin.WxOpen.AdvancedAPIs.Sns;

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
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "AppSettings:WechatAppId", "mock-app-id" },
                        { "AppSettings:WechatAppSecret", "12345678901234567890123456789012" }
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
            .Setup(s => s.CodeToSessionAsync(It.IsAny<string>(), It.IsAny<string>(), code))
            .ReturnsAsync(new JsCode2JsonResult
            {
                errcode = Senparc.Weixin.ReturnCode.请求成功,
                openid = openId,
                session_key = sessionKey
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
            .Setup(s => s.CodeToSessionAsync(It.IsAny<string>(), It.IsAny<string>(), code))
            .ReturnsAsync(new JsCode2JsonResult
            {
                errcode = (Senparc.Weixin.ReturnCode)40029,
                errmsg = "Invalid code"
            });

        var model = new Code2SessionDto { Code = code };

        // Act
        var response = await _client.PostAsJsonAsync("/api/Auth/login", model);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
