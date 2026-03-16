using System.Net;
// Redundant using removed
using Aiursoft.CSTools.Tools;
// Redundant using removed
// Redundant using removed
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.IntegrationTests;
// Redundant using removed
// Redundant using removed
using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class PaymentIntegrationTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public PaymentIntegrationTests()
    {
        _port = Network.GetAvailablePort();
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        TestStartupWithMockWeChat.MockWeChatPayService = new Mock<IWeChatPayService>();
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task TestCleanup()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    [TestMethod]
    public async Task TestCreateOrderUnauthorized()
    {
        _ = await _http.PostAsJsonAsync("/api/payment/create-order", new CreatePaymentOrderRequest
        {
            VipProductId = Guid.NewGuid()
        });
    }

    [TestMethod]
    public async Task TestGetVipStatusUnauthorized()
    {
        var response = await _http.GetAsync("/api/payment/vip-status");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task TestPaymentNotifySuccess()
    {
        // Mock the service to return true
        TestStartupWithMockWeChat.MockWeChatPayService!
            .Setup(s => s.HandlePaymentNotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payment/notify")
        {
            Content = JsonContent.Create(new { some = "data" })
        };
        request.Headers.Add("Wechatpay-Signature", "sig");
        request.Headers.Add("Wechatpay-Timestamp", "123");
        request.Headers.Add("Wechatpay-Nonce", "nonce");
        request.Headers.Add("Wechatpay-Serial", "serial");

        var response = await _http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var notifyResult = await response.Content.ReadFromJsonAsync<NotifyResponse>();
        Assert.IsNotNull(notifyResult);
        Assert.AreEqual("SUCCESS", notifyResult.code);
    }

    public class NotifyResponse
    {
        public string code { get; set; } = string.Empty;
        public string message { get; set; } = string.Empty;
    }

    [TestMethod]
    public async Task TestPaymentNotifyFailure()
    {
        // Mock the service to return false
        TestStartupWithMockWeChat.MockWeChatPayService!
            .Setup(s => s.HandlePaymentNotifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payment/notify")
        {
            Content = JsonContent.Create(new { some = "data" })
        };
        
        var response = await _http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
