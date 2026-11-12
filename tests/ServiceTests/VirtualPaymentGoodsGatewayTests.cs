using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentGoodsGatewayTests
{
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(0, true)]
    [DataRow(1, false)]
    [DataRow(1, true)]
    public async Task UsesExistingSdkExactSignaturesAndExplicitEnvironment(int environment, bool publish)
    {
        using var handler = new GoodsHandler();
        var options = Settings(environment);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new GoodsFactory(handler);
        using var tokens = new VirtualPaymentAccessTokenService(options, factory, cache);
        var gateway = new VirtualPaymentGoodsGateway(options, factory, tokens);
        var goods = Goods(environment);
        await gateway.StartAsync(goods, publish, CancellationToken.None);
        var result = await gateway.QueryAsync(publish, CancellationToken.None);
        Assert.AreEqual(3, result.Status); Assert.AreEqual("vp_test", result.Items.Single().Id);
        Assert.AreEqual(2, result.Items.Single().Status);
        if (!publish)
        {
            Assert.AreEqual(goods.Name, result.Items.Single().Name);
            Assert.AreEqual(goods.PriceInFen, result.Items.Single().Price);
            Assert.AreEqual(goods.Remark, result.Items.Single().Remark);
        }
        Assert.AreEqual(1, handler.TokenRequests); Assert.HasCount(2, handler.Requests);
        foreach (var (uri, body) in handler.Requests)
        {
            Assert.AreEqual("api.weixin.qq.com", uri.Host);
            var query = QueryHelpers.ParseQuery(uri.Query);
            Assert.AreEqual("test-token", query["access_token"].ToString());
            Assert.AreEqual(VirtualPaymentSignatures.SignPayment(uri.AbsolutePath, body,
                environment == 0 ? "live-test-key" : "sandbox-test-key"), query["pay_sig"].ToString());
            using var json = JsonDocument.Parse(body);
            Assert.AreEqual(environment, json.RootElement.GetProperty("env").GetInt32());
            Assert.DoesNotContain("test-key", body);
        }
        using var start = JsonDocument.Parse(handler.Requests[0].Body);
        var item = start.RootElement.GetProperty(publish ? "publish_item" : "upload_item")[0];
        Assert.AreEqual(goods.GoodsId, item.GetProperty("id").GetString());
        if (!publish)
        {
            Assert.AreEqual(goods.Name, item.GetProperty("name").GetString());
            Assert.AreEqual(goods.PriceInFen, item.GetProperty("price").GetInt32());
            Assert.AreEqual(goods.PictureUrl, item.GetProperty("item_url").GetString());
        }
    }

    [TestMethod]
    [DataRow("{}")]
    [DataRow("{\"errcode\":268490002,\"errmsg\":\"private-credential-must-not-leak\"}")]
    public async Task MissingSuccessAndGatewayErrorCannotBeTreatedAsPublished(string response)
    {
        using var handler = new GoodsHandler { ErrorResponse = response };
        using var cache = new MemoryCache(new MemoryCacheOptions()); var options = Settings(0);
        var factory = new GoodsFactory(handler);
        using var tokens = new VirtualPaymentAccessTokenService(options, factory, cache);
        var gateway = new VirtualPaymentGoodsGateway(options, factory, tokens);
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.QueryAsync(false, CancellationToken.None));
        Assert.DoesNotContain("private-credential", ex.ToString()); Assert.DoesNotContain("test-token", ex.ToString());
    }

    [TestMethod]
    public async Task RejectedCachedTokenIsEvictedWithoutForcingOtherClientsToRefresh()
    {
        using var handler = new GoodsHandler { ErrorResponse = "{\"errcode\":40014,\"errmsg\":\"invalid token\"}" };
        using var cache = new MemoryCache(new MemoryCacheOptions()); var options = Settings(0);
        var factory = new GoodsFactory(handler);
        using var tokens = new VirtualPaymentAccessTokenService(options, factory, cache);
        var gateway = new VirtualPaymentGoodsGateway(options, factory, tokens);
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => gateway.QueryAsync(false, CancellationToken.None));
        Assert.AreEqual(2, handler.TokenRequests);
    }

    private static VirtualPaymentGoods Goods(int environment) => new()
    {
        SourceProductId = Guid.NewGuid(), AppId = "app", OfferId = "offer", Environment = environment,
        Fingerprint = "test", GoodsId = "vp_test", Name = "中国音乐史", PriceInFen = 19800, DurationDays = 365,
        Remark = "Membership access for 365 days.", PictureUrl = "https://example.test/logo.png"
    };
    private static IOptions<AppSettings> Settings(int environment) => Options.Create(new AppSettings
    {
        AuthProvider = "Local", OIDC = new() { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" }, Local = new(),
        WeChat = new() { AppId = "app", AppSecret = "test-secret", VirtualPayment = new()
        { Environment = environment, OfferId = "offer", AppKey = "live-test-key", SandboxAppKey = "sandbox-test-key" } }
    });
    private sealed class GoodsFactory(GoodsHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
    private sealed class GoodsHandler : HttpMessageHandler
    {
        public int TokenRequests { get; private set; }
        public List<(Uri Uri, string Body)> Requests { get; } = [];
        public string? ErrorResponse { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            string response;
            if (request.RequestUri!.AbsolutePath == "/cgi-bin/stable_token")
            {
                TokenRequests++;
                using var json = JsonDocument.Parse(body);
                Assert.IsFalse(json.RootElement.GetProperty("force_refresh").GetBoolean());
                response = "{\"errcode\":0,\"access_token\":\"test-token\",\"expires_in\":7200}";
            }
            else
            {
                Requests.Add((request.RequestUri, body));
                response = ErrorResponse ?? (request.RequestUri.AbsolutePath.Contains("query_upload")
                    ? "{\"errcode\":0,\"status\":3,\"upload_item\":[{\"id\":\"vp_test\",\"name\":\"中国音乐史\",\"price\":19800,\"remark\":\"Membership access for 365 days.\",\"upload_status\":2}]}"
                    : request.RequestUri.AbsolutePath.Contains("query_publish")
                        ? "{\"errcode\":0,\"status\":3,\"publish_item\":[{\"id\":\"vp_test\",\"publish_status\":2}]}"
                        : "{\"errcode\":0}");
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
