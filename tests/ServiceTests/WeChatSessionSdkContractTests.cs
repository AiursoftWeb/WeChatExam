using System.Net;
using System.Text;
using System.Text.Json;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.WebUtilities;
using SKIT.FlurlHttpClient.Wechat.Api;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class WeChatSessionSdkContractTests
{
    private const string AppId = "synthetic-app";
    private const string Secret = "synthetic-secret+/&=";
    private const string Code = "synthetic-code+/&=?";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LoginUsesTheRealSdkAndPreservesEncodedQueryAndSessionFields(bool hasUnionId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            openid = "synthetic-open-id", session_key = "synthetic-session+/=",
            unionid = hasUnionId ? "synthetic-union-id" : null
        });
        using var handler = new SessionHandler(payload);
        using var http = new HttpClient(handler);
        using var client = new SessionClient(http);
        var result = await new WeChatService(client).CodeToSessionAsync(Code);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("synthetic-open-id", result.OpenId);
        Assert.AreEqual("synthetic-session+/=", result.SessionKey);
        Assert.AreEqual(hasUnionId ? "synthetic-union-id" : null, result.UnionId);
        Assert.AreEqual(0, result.ErrorCode);
        Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual(HttpMethod.Get, handler.Method);
        Assert.AreEqual("https://api.weixin.qq.com/sns/jscode2session", handler.Uri!.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(handler.Uri.Query);
        Assert.HasCount(4, query);
        Assert.AreEqual(AppId, query["appid"].ToString());
        Assert.AreEqual(Secret, query["secret"].ToString());
        Assert.AreEqual(Code, query["js_code"].ToString());
        Assert.AreEqual("authorization_code", query["grant_type"].ToString());
        Assert.IsTrue(string.IsNullOrEmpty(handler.Body));
    }

    [TestMethod]
    [DataRow(40029)]
    [DataRow(40163)]
    public async Task InvalidOrConsumedLoginCodesCannotAuthenticate(int errorCode)
    {
        using var handler = new SessionHandler(JsonSerializer.Serialize(new { errcode = errorCode, errmsg = "synthetic-invalid-code" }));
        using var http = new HttpClient(handler);
        using var client = new SessionClient(http);
        var result = await new WeChatService(client).CodeToSessionAsync(Code);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(errorCode, result.ErrorCode);
        Assert.AreEqual("synthetic-invalid-code", result.ErrorMessage);
        Assert.IsTrue(string.IsNullOrEmpty(result.OpenId));
        Assert.IsTrue(string.IsNullOrEmpty(result.SessionKey));
        Assert.AreEqual(1, handler.Calls);
    }

    [TestMethod]
    [DataRow(null, "synthetic-session")]
    [DataRow("", "synthetic-session")]
    [DataRow(" ", "synthetic-session")]
    [DataRow("synthetic-open-id", null)]
    [DataRow("synthetic-open-id", "")]
    [DataRow("synthetic-open-id", " ")]
    public async Task AnIncompleteSuccessResponseCannotAuthenticate(string? openId, string? sessionKey)
    {
        using var handler = new SessionHandler(JsonSerializer.Serialize(new { openid = openId, session_key = sessionKey }));
        using var http = new HttpClient(handler);
        using var client = new SessionClient(http);
        var result = await new WeChatService(client).CodeToSessionAsync(Code);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(1, handler.Calls);
    }

    private sealed class SessionClient(HttpClient http) : WechatApiClient(
        new WechatApiClientOptions { AppId = AppId, AppSecret = Secret }, http);

    private sealed class SessionHandler(string payload) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
