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
public class VirtualPaymentRefundGatewayTests
{
    private static (PaymentOrder Order, VirtualPaymentSnapshot Snapshot, VirtualPaymentRefundRequest Refund) Purchase()
    {
        var order = new PaymentOrder { OutTradeNo = "SYNTHETIC-PAID", UserId = "buyer", Description = "Synthetic",
            Channel = PaymentChannel.WeChatVirtual, AmountInFen = 200, DiscountInFen = 19600, Status = PaymentOrderStatus.Paid };
        return (order, new VirtualPaymentSnapshot { PaymentOrderId = order.Id, AppId = "test-app", OpenId = "original-buyer",
            PlatformOrderId = "platform-paid", Platform = VirtualPaymentPlatform.WeChat, SignData = JsonSerializer.Serialize(new {
                offerId = "offer", buyQuantity = 1, env = 0, currencyType = "CNY", productId = "course",
                goodsPrice = 19800, activitySellingPrice = 200, outTradeNo = order.OutTradeNo, attach = order.Id.ToString("N") }) },
            new VirtualPaymentRefundRequest { PaymentOrderId = order.Id, ActorId = "operator", AmountInFen = 200, RefundOrderId = "SYNTHETIC-REFUND" });
    }
    private static VirtualPaymentRefundGateway Gateway(Handler handler) => new(Options.Create(new AppSettings {
        AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = false,
        OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
        WeChat = new WeChatSettings { AppId = "test-app", VirtualPayment = new VirtualPaymentSettings { OfferId = "offer", AppKey = "synthetic-live-key" } }
    }), new Factory(handler));

    [TestMethod]
    public async Task SubmissionUsesExactSdkSigningOriginalBuyerAndFullActualPayment()
    {
        var p = Purchase();
        using var handler = new Handler(new JsonObject { ["errcode"] = 0, ["refund_order_id"] = p.Refund.RefundOrderId,
            ["refund_wx_order_id"] = "platform-refund", ["pay_order_id"] = p.Order.OutTradeNo, ["pay_wx_order_id"] = "platform-paid" });
        var response = await Gateway(handler).StartAsync(p.Order, p.Snapshot, p.Refund, "synthetic-token");
        Assert.IsTrue(response.Accepted); Assert.AreEqual("platform-refund", response.PlatformRefundId); Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual("/xpay/refund_order", handler.Uri!.AbsolutePath);
        var query = QueryHelpers.ParseQuery(handler.Uri.Query);
        Assert.HasCount(2, query); Assert.AreEqual("synthetic-token", query["access_token"].ToString());
        Assert.AreEqual(Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("synthetic-live-key"),
            Encoding.UTF8.GetBytes("/xpay/refund_order&" + handler.Body))), query["pay_sig"].ToString());
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.AreEqual("original-buyer", (string?)body["openid"]); Assert.AreEqual(0, (int)body["env"]!);
        Assert.AreEqual(p.Order.OutTradeNo, (string?)body["order_id"]); Assert.AreEqual(200, (int)body["left_fee"]!);
        Assert.AreEqual(200, (int)body["refund_fee"]!); Assert.AreEqual("5", (string?)body["refund_reason"]);
        Assert.AreEqual("1", (string?)body["req_from"]); Assert.AreEqual(VirtualPaymentRefundGateway.Metadata(p.Refund), (string?)body["biz_meta"]);
        Assert.DoesNotContain("secret", handler.Body!);
    }

    [TestMethod]
    [DataRow(5, VirtualRefundState.Pending)]
    [DataRow(7, VirtualRefundState.Failed)]
    [DataRow(8, VirtualRefundState.Completed)]
    public async Task OnlyAnExactRefundOrderCanBeReportedAsCompleted(int status, VirtualRefundState state)
    {
        var p = Purchase();
        using var handler = new Handler(RefundResponse(p.Refund, status));
        Assert.AreEqual(state, (await Gateway(handler).QueryAsync(p.Order, p.Snapshot, p.Refund, "token")).State);
        Assert.AreEqual(p.Refund.RefundOrderId, (string?)JsonNode.Parse(handler.Body!)!["order_id"]);
    }

    [TestMethod]
    [DataRow("order")]
    [DataRow("type")]
    [DataRow("environment")]
    [DataRow("amount")]
    [DataRow("metadata")]
    [DataRow("reference")]
    public async Task RefundQueryCannotBindToAnotherPurchaseOrAmount(string fault)
    {
        var p = Purchase(); var response = RefundResponse(p.Refund, 8);
        if (fault == "order") response["order"]!["order_id"] = "other-order";
        if (fault == "type") response["order"]!["order_type"] = 0;
        if (fault == "environment") response["order"]!["env_type"] = 2;
        if (fault == "amount") response["order"]!["refund_fee"] = 19800;
        if (fault == "metadata") response["order"]!["biz_meta"] = "other-purchase";
        if (fault == "reference") p.Refund.PlatformRefundId = "other-platform-refund";
        using var handler = new Handler(response);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(handler).QueryAsync(p.Order, p.Snapshot, p.Refund, "token"));
    }

    [TestMethod]
    public async Task AppleNeverUsesTheMerchantRefundApiAndTransportLossNeverRetries()
    {
        var p = Purchase();
        using var handler = new Handler(new JsonObject()) { LoseResponse = true };
        p.Snapshot.Platform = VirtualPaymentPlatform.Apple;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(handler).StartAsync(p.Order, p.Snapshot, p.Refund, "token"));
        Assert.AreEqual(0, handler.Calls);
        p.Snapshot.Platform = VirtualPaymentPlatform.WeChat;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Gateway(handler).StartAsync(p.Order, p.Snapshot, p.Refund, "token"));
        Assert.AreEqual(1, handler.Calls);
    }

    private static JsonObject RefundResponse(VirtualPaymentRefundRequest refund, int status) => new() {
        ["errcode"] = 0, ["order"] = new JsonObject { ["order_id"] = refund.RefundOrderId, ["wx_order_id"] = "platform-refund",
            ["order_type"] = 1, ["status"] = status, ["env_type"] = 1, ["biz_type"] = 1000, ["refund_fee"] = 200,
            ["biz_meta"] = VirtualPaymentRefundGateway.Metadata(refund) } };

    private sealed class Factory(Handler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class Handler(JsonObject response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Body { get; private set; }
        public bool LoseResponse { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Uri = request.RequestUri; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (LoseResponse) throw new HttpRequestException("Synthetic private URL " + Uri);
            return new(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString()) };
        }
    }
}
