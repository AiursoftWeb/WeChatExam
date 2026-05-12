using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class DebugModeTests
{
    private int _port;
    private HttpClient _http = null!;
    private IHost? _server;

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
        _http.Dispose();
    }

    private async Task StartServer(bool debugMode)
    {
        _port = Network.GetAvailablePort();
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };

        TestStartupWithMockWeChat.MockWeChatService = new Mock<IWeChatService>();
        TestStartupWithMockWeChat.MockDistributionChannelService = new Mock<IDistributionChannelService>();
        TestStartupWithMockWeChat.MockWeChatPayService = new Mock<IWeChatPayService>();
        TestStartupWithMockWeChat.MockOllamaService = new Mock<IOllamaService>();
        TestStartupWithMockWeChat.MockVipProductService = new Mock<IVipProductService>();

        // Pass configuration override via command line arguments
        _server = await AppAsync<TestStartupWithMockWeChat>(
            [
                "--AppSettings:DebugMode=" + debugMode.ToString()
            ],
            port: _port);

        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
    }

    [TestMethod]
    public async Task ExchangeDebugToken_DebugModeOff_ReturnsNotFound()
    {
        // Arrange
        await StartServer(false);
        var model = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345"
        };

        // Act
        var response = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", model);

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ExchangeDebugToken_DebugModeOn_ReturnsToken()
    {
        // Arrange
        await StartServer(true);
        var model = new DebugTokenRequestDto
        {
            MagicKey = "test-magic-key-12345" // Match what's in tests/appsettings.json
        };

        // Act
        var response = await _http.PostAsJsonAsync("/api/Auth/exchange_debug_token", model);

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = await response.Content.ReadFromJsonAsync<TokenDto>();
        Assert.IsNotNull(token);
        Assert.IsFalse(string.IsNullOrEmpty(token.Token));
    }
}
