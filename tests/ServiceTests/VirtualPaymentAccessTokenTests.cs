using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentAccessTokenTests
{
    private const string Secret = "synthetic-app-secret+/&=";
    private const string Token = "synthetic-stable-token+/&=";

    [TestMethod]
    public async Task ConcurrentQueriesShareStableTokenWithoutForceRefreshOrNewSalesEnabled()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(delay: 20);
        using var service = Service(cache, handler);
        var pending = new List<Task<string>>();
        for (var i = 0; i < 8; i++) pending.Add(service.GetAsync("app-a"));
        var tokens = await Task.WhenAll(pending);
        Assert.IsTrue(tokens.All(t => t == Token));
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual("https://api.weixin.qq.com/cgi-bin/stable_token", handler.Uri!.AbsoluteUri);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual("app-a", body.RootElement.GetProperty("appid").GetString());
        Assert.AreEqual(Secret, body.RootElement.GetProperty("secret").GetString());
        Assert.AreEqual("client_credential", body.RootElement.GetProperty("grant_type").GetString());
        Assert.IsFalse(body.RootElement.GetProperty("force_refresh").GetBoolean());
        Assert.DoesNotContain("access_token", handler.Body!);
    }

    [TestMethod]
    public async Task SharedCacheDoesNotMixApplicationsOrCredentials()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var a = new TokenHandler();
        using var b = new TokenHandler(token: "second-token");
        using var c = new TokenHandler(token: "rotated-token");
        using var first = Service(cache, a);
        using var second = Service(cache, b, appId: "app-b");
        using var rotated = Service(cache, c, secret: "rotated-secret");
        Assert.AreEqual(Token, await first.GetAsync("app-a"));
        Assert.AreEqual("second-token", await second.GetAsync("app-b"));
        Assert.AreEqual("rotated-token", await rotated.GetAsync("app-a"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first.GetAsync("app-b"));
        Assert.AreEqual(1, a.Calls);
        Assert.AreEqual(1, b.Calls);
        Assert.AreEqual(1, c.Calls);
    }

    [TestMethod]
    public async Task EvictedCredentialIsFetchedAgainInOrdinaryMode()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler();
        using var service = Service(cache, handler);
        await service.GetAsync("app-a");
        cache.Compact(1);
        await service.GetAsync("app-a");
        Assert.AreEqual(2, handler.Calls);
        Assert.DoesNotContain("true", handler.Body!);
    }

    [TestMethod]
    public async Task ShortRemainingLifetimeIsNotCachedForTwoHours()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(expiresIn: 1);
        using var service = Service(cache, handler);
        Assert.AreEqual(Token, await service.GetAsync("app-a"));
        Assert.AreEqual(Token, await service.GetAsync("app-a"));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    [DataRow(345)]
    [DataRow(7200)]
    public async Task ValidRemainingLifetimeCanBeCached(int seconds)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(expiresIn: seconds);
        using var service = Service(cache, handler);
        await service.GetAsync("app-a");
        await service.GetAsync("app-a");
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow("app", "secret", "")]
    [DataRow("", "secret", "app-a")]
    [DataRow("app-a", "", "app-a")]
    [DataRow("app-a", "secret", "other-app")]
    public async Task InvalidConfigurationOrWrongAppNeverMakesRequests(string app, string secret, string requested)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler();
        using var service = Service(cache, handler, app, secret);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetAsync(requested));
        Assert.AreEqual(0, handler.Calls);
    }

    [TestMethod]
    [DataRow("error")]
    [DataRow("empty")]
    [DataRow("zero")]
    [DataRow("negative")]
    [DataRow("missing-expiry")]
    [DataRow("malformed")]
    [DataRow("http")]
    [DataRow("network")]
    public async Task FailuresAreNotCachedAndDoNotLeakCredentials(string fault)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(fault: fault);
        using var service = Service(cache, handler);
        for (var i = 0; i < 2; i++)
        {
            var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.GetAsync("app-a"));
            Assert.DoesNotContain(Token, ex.ToString());
            Assert.DoesNotContain(Secret, ex.ToString());
        }
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task CancelledRequestDoesNotPoisonFutureRequests()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(delay: 100);
        using var service = Service(cache, handler);
        using var cancellation = new CancellationTokenSource();
        var pending = service.GetAsync("app-a", cancellation.Token);
        await handler.Started.Task;
        await cancellation.CancelAsync();
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain(Secret, ex.ToString());
        Assert.AreEqual(Token, await service.GetAsync("app-a"));
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task CancellingOneWaiterDoesNotCancelAnotherBuyersQuery()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler(delay: 100);
        using var service = Service(cache, handler);
        var first = service.GetAsync("app-a");
        await handler.Started.Task;
        using var cancellation = new CancellationTokenSource();
        var waiter = service.GetAsync("app-a", cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => waiter);
        Assert.AreEqual(Token, await first);
        Assert.AreEqual(Token, await service.GetAsync("app-a"));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    public async Task DelayedRejectionCannotEvictANewerCachedCredential()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var handler = new TokenHandler();
        using var service = Service(cache, handler);
        await service.GetAsync("app-a");
        await service.ForgetRejectedAsync("app-a", "stale-token");
        await service.GetAsync("app-a");
        Assert.AreEqual(1, handler.Calls);
        await service.ForgetRejectedAsync("app-a", Token);
        await service.GetAsync("app-a");
        Assert.AreEqual(2, handler.Calls);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.ForgetRejectedAsync("another-app", Token));
    }

    private static VirtualPaymentAccessTokenService Service(IMemoryCache cache, TokenHandler handler,
        string appId = "app-a", string secret = Secret) => new(Options.Create(new AppSettings
        {
            AuthProvider = "Local", Local = new LocalSettings(),
            OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "",
                UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
            WeChat = new WeChatSettings { AppId = appId, AppSecret = secret }
        }), new TokenFactory(handler), cache);

    private sealed class TokenFactory(TokenHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TokenHandler(string token = Token, int expiresIn = 7200, string? fault = null, int delay = 0)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Uri = request.RequestUri;
            Method = request.Method;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Started.TrySetResult();
            if (delay > 0) await Task.Delay(delay, cancellationToken);
            if (fault == "network") throw new HttpRequestException("Failure with " + Secret);
            var response = new JsonObject
            {
                ["access_token"] = fault == "empty" ? " " : token,
                ["expires_in"] = fault switch { "zero" => 0, "negative" => -1, _ => expiresIn }
            };
            if (fault == "error")
            {
                response["errcode"] = 40164;
                response["errmsg"] = Secret + Token;
            }
            if (fault == "missing-expiry") response.Remove("expires_in");
            return new HttpResponseMessage(fault == "http" ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
            {
                Content = new StringContent(fault == "malformed" ? "not json " + Secret : response.ToJsonString(), Encoding.UTF8, "application/json")
            };
        }
    }
}
