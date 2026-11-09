using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentQueryTests
{
    private const string Token = "query-token+/&=secret";
    private const string OrderNumber = "ORDER-20261109-QUERY";
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(0, 7)]
    [DataRow(1, 0)]
    [DataRow(1, 7)]
    public async Task QueriesSavedEnvironmentAndSignsExactWireBodyWithoutLogin(int environment, int orderType)
    {
        var order = CreateOrder();
        var settings = CreateSettings(environment);
        var snapshot = Capture(order, settings);
        // New sales are off, catalog is gone and the active environment changed.
        // Reconciliation still uses the original snapshot, without a User/session.
        settings = CreateSettings(1 - environment, enabled: false, products: false);
        using var handler = new QueryHandler(Response(environment, orderType));
        var result = await CreateService(settings, handler).QueryAsync(order, snapshot, Token);
        Assert.AreEqual(VirtualPaymentOrderState.PaidAwaitingDelivery, result.State);
        Assert.AreEqual("platform-order", result.PlatformOrderId);
        Assert.AreEqual(orderType == 7 ? null : "wechat-transaction", result.TransactionId);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.IsNotNull(handler.Uri);
        Assert.AreEqual("https://api.weixin.qq.com/xpay/query_order", handler.Uri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(handler.Uri.Query);
        Assert.AreEqual(Token, query["access_token"].ToString());
        Assert.HasCount(2, query);
        Assert.IsNotNull(handler.Body);
        var key = environment == 1 ? "sandbox-secret" : "live-secret";
        var independentSignature = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes("/xpay/query_order&" + handler.Body)));
        Assert.AreEqual(independentSignature, query["pay_sig"].ToString());
        using var body = JsonDocument.Parse(handler.Body);
        Assert.AreEqual("saved-openid", body.RootElement.GetProperty("openid").GetString());
        Assert.AreEqual(environment, body.RootElement.GetProperty("env").GetInt32());
        Assert.AreEqual(OrderNumber, body.RootElement.GetProperty("order_id").GetString());
        Assert.DoesNotContain("session", handler.Body);
        Assert.DoesNotContain(Token, handler.Body);
        Assert.DoesNotContain("secret", handler.Body);
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.IsNull(order.PaidAt);
        Assert.IsNull(order.WechatTransactionId);
    }

    [TestMethod]
    [DataRow(0, VirtualPaymentOrderState.Initializing)]
    [DataRow(1, VirtualPaymentOrderState.Created)]
    [DataRow(2, VirtualPaymentOrderState.PaidAwaitingDelivery)]
    [DataRow(3, VirtualPaymentOrderState.Delivering)]
    [DataRow(4, VirtualPaymentOrderState.Delivered)]
    [DataRow(5, VirtualPaymentOrderState.RefundOrReversal)]
    [DataRow(6, VirtualPaymentOrderState.Closed)]
    [DataRow(7, VirtualPaymentOrderState.RefundOrReversal)]
    [DataRow(8, VirtualPaymentOrderState.RefundOrReversal)]
    [DataRow(9, VirtualPaymentOrderState.RefundOrReversal)]
    [DataRow(10, VirtualPaymentOrderState.RefundOrReversal)]
    public async Task MapsProtocolStatesWithoutCastingToLocalOrderStatus(int status, VirtualPaymentOrderState expected)
    {
        var settings = CreateSettings();
        var order = CreateOrder();
        using var handler = new QueryHandler(Response(status: status));
        var result = await CreateService(settings, handler).QueryAsync(order, Capture(order, settings), Token);
        Assert.AreEqual(expected, result.State);
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
    }

    [TestMethod]
    [DataRow(2, false)]
    [DataRow(3, false)]
    [DataRow(4, false)]
    [DataRow(2, true)]
    [DataRow(3, true)]
    [DataRow(4, true)]
    public async Task PartiallyRefundedPaymentCannotLookLikeFreshPayment(int status, bool onlyRemainingBalance)
    {
        var response = Response(status: status);
        response["order"]![onlyRemainingBalance ? "left_fee" : "refund_fee"] = onlyRemainingBalance ? 6700 : 100;
        var settings = CreateSettings();
        var order = CreateOrder();
        using var handler = new QueryHandler(response);
        var result = await CreateService(settings, handler).QueryAsync(order, Capture(order, settings), Token);
        Assert.AreEqual(VirtualPaymentOrderState.RefundOrReversal, result.State);
    }

    [TestMethod]
    [DataRow("order_id", "other-order")]
    [DataRow("env_type", 0)]
    [DataRow("env_type", 1)]
    [DataRow("env_type", 3)]
    [DataRow("order_type", 1)]
    [DataRow("order_type", 8)]
    [DataRow("order_type", 99)]
    [DataRow("biz_type", 1)]
    [DataRow("order_fee", 19800)]
    [DataRow("order_fee", 6799)]
    [DataRow("order_fee", 0)]
    [DataRow("wx_order_id", "")]
    [DataRow("refund_fee", -1)]
    [DataRow("refund_fee", 6801)]
    [DataRow("left_fee", -1)]
    [DataRow("left_fee", 6801)]
    [DataRow("status", 99)]
    public async Task RejectsMismatchedOrUnknownPlatformOrders(string field, object value)
    {
        var response = Response();
        response["order"]![field] = JsonSerializer.SerializeToNode(value);
        var settings = CreateSettings();
        var order = CreateOrder();
        using var handler = new QueryHandler(response);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            CreateService(settings, handler).QueryAsync(order, Capture(order, settings), Token));
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
    }

    [TestMethod]
    [DataRow("app")]
    [DataRow("order")]
    [DataRow("openid")]
    [DataRow("json")]
    [DataRow("amount")]
    [DataRow("environment")]
    [DataRow("offer")]
    [DataRow("key")]
    [DataRow("channel")]
    [DataRow("token")]
    [DataRow("missing-env")]
    public async Task RejectsInvalidLocalPurchasesBeforeAnyNetworkCall(string fault)
    {
        var settings = CreateSettings();
        var order = CreateOrder();
        var snapshot = Capture(order, settings);
        var json = JsonNode.Parse(snapshot.SignData)!;
        if (fault == "amount") json["activitySellingPrice"] = 1;
        if (fault == "environment") json["env"] = 2;
        if (fault == "offer") json["offerId"] = "another-offer";
        if (fault == "missing-env") json.AsObject().Remove("env");
        snapshot = new VirtualPaymentSnapshot
        {
            PaymentOrderId = fault == "order" ? Guid.NewGuid() : snapshot.PaymentOrderId,
            AppId = fault == "app" ? "another-app" : snapshot.AppId,
            OpenId = fault == "openid" ? "" : snapshot.OpenId,
            SignData = fault == "json" ? "invalid" : json.ToJsonString()
        };
        if (fault == "key") settings = CreateSettings(sandboxKey: "");
        if (fault == "channel") order = CreateOrder(channel: PaymentChannel.WeChatJsApi);
        using var handler = new QueryHandler(Response());
        var service = CreateService(settings, handler);
        if (fault == "token")
            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.QueryAsync(order, snapshot, " "));
        else
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.QueryAsync(order, snapshot, Token));
        Assert.AreEqual(0, handler.CallCount);
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Pending)]
    [DataRow(PaymentOrderStatus.Paid)]
    [DataRow(PaymentOrderStatus.Refunded)]
    public async Task LateQueryDoesNotRequireUnexpiredPendingOrderOrMutateFinalState(PaymentOrderStatus status)
    {
        var settings = CreateSettings();
        var order = CreateOrder();
        var snapshot = Capture(order, settings);
        order = CreateOrder(expired: true);
        order.Status = status;
        using var handler = new QueryHandler(Response());
        var result = await CreateService(settings, handler).QueryAsync(order, snapshot, Token);
        Assert.AreEqual(VirtualPaymentOrderState.PaidAwaitingDelivery, result.State);
        Assert.AreEqual(status, order.Status);
        Assert.IsNull(order.PaidAt);
    }

    [TestMethod]
    [DataRow("error")]
    [DataRow("missing-order")]
    [DataRow("malformed")]
    [DataRow("http")]
    [DataRow("network")]
    public async Task FailuresNeverReturnSuccessOrExposeCredentials(string fault)
    {
        var settings = CreateSettings();
        var order = CreateOrder();
        var response = Response();
        if (fault == "error")
        {
            response["errcode"] = 40001;
            response["errmsg"] = "sensitive " + Token;
        }
        if (fault == "missing-order") response.Remove("order");
        using var handler = new QueryHandler(response, fault == "http" ? HttpStatusCode.BadGateway : HttpStatusCode.OK,
            fault == "malformed" ? "not json " + Token : null, networkFailure: fault == "network");
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            CreateService(settings, handler).QueryAsync(order, Capture(order, settings), Token));
        Assert.DoesNotContain(Token, ex.ToString());
        Assert.DoesNotContain(Uri.EscapeDataString(Token), ex.ToString());
        Assert.DoesNotContain("sandbox-secret", ex.ToString());
        Assert.DoesNotContain("saved-openid", ex.ToString());
    }

    [TestMethod]
    public async Task CancellationIsPreservedWithoutLeakingRequestUrl()
    {
        var settings = CreateSettings();
        var order = CreateOrder();
        using var handler = new QueryHandler(Response());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService(settings, handler)
            .QueryAsync(order, Capture(order, settings), Token, cancellation.Token));
        Assert.DoesNotContain(Token, ex.ToString());
        Assert.DoesNotContain(Uri.EscapeDataString(Token), ex.ToString());
    }

    private static JsonObject Response(int environment = 1, int orderType = 0, int status = 2) => new()
    {
        ["errcode"] = 0,
        ["order"] = new JsonObject
        {
            ["order_id"] = OrderNumber, ["env_type"] = environment + 1, ["order_type"] = orderType,
            ["status"] = status, ["biz_type"] = 0, ["order_fee"] = 6800,
            // Platform-funded discount does not alter the merchant order amount.
            ["paid_fee"] = 6700, ["wx_order_id"] = "platform-order",
            ["wxpay_order_id"] = orderType == 7 ? null : "wechat-transaction"
        }
    };

    private static PaymentOrder CreateOrder(bool expired = false, PaymentChannel channel = PaymentChannel.WeChatVirtual) => new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), UserId = "buyer", Channel = channel,
        OutTradeNo = OrderNumber, VipProductId = ProductId, Description = "membership", AmountInFen = 6800,
        DiscountInFen = 13000, ExpiredAt = expired ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(15)
    };

    private static VirtualPaymentSettings CreateSettings(int environment = 1, bool enabled = true,
        bool products = true, string sandboxKey = "sandbox-secret") => new()
    {
        Enabled = enabled, Environment = environment, OfferId = "123", AppKey = "live-secret", SandboxAppKey = sandboxKey,
        Products = products ? new Dictionary<string, VirtualPaymentProductSettings>
        { [ProductId.ToString()] = new() { ProductId = "membership", PriceInFen = 19800 } } : new()
    };

    private static IOptions<AppSettings> OptionsFor(VirtualPaymentSettings settings) => Options.Create(new AppSettings
    {
        AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = true,
        OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "",
            UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
        WeChat = new WeChatSettings { AppId = "test-app", VirtualPayment = settings }
    });

    private static VirtualPaymentSnapshot Capture(PaymentOrder order, VirtualPaymentSettings settings) =>
        new VirtualPaymentParameterService(OptionsFor(settings)).Capture(order, new User
        { Id = "buyer", DisplayName = "Buyer", MiniProgramOpenId = "saved-openid", SessionKey = "not-needed-for-query" });

    private static VirtualPaymentQueryService CreateService(VirtualPaymentSettings settings, QueryHandler handler) =>
        new(OptionsFor(settings), new QueryHttpClientFactory(handler));

    private sealed class QueryHttpClientFactory(QueryHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class QueryHandler(JsonObject response, HttpStatusCode status = HttpStatusCode.OK, string? rawBody = null, bool networkFailure = false) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (networkFailure) throw new HttpRequestException("Failed " + request.RequestUri);
            CallCount++;
            Method = request.Method;
            Uri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(rawBody ?? response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
