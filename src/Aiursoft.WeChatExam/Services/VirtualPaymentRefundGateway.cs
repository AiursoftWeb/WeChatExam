using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

public interface IVirtualPaymentRefundGateway
{
    Task<VirtualRefundSubmission> StartAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default);
    Task<VirtualRefundObservation> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default);
}

/// <summary>Uses the installed SDK's exact-body signing; never retries a refund submission.</summary>
public class VirtualPaymentRefundGateway(IOptions<AppSettings> options, IHttpClientFactory clients)
    : IVirtualPaymentRefundGateway, IScopedDependency
{
    public async Task<VirtualRefundSubmission> StartAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default)
    {
        var (purchase, metadata) = Validate(order, snapshot, refund);
        if (snapshot.Platform != VirtualPaymentPlatform.WeChat)
            throw new InvalidOperationException("Only a verified non-Apple purchase can be refunded here.");
        using var client = Client(snapshot, purchase.Env);
        try
        {
            var response = await client.ExecuteXPayRefundOrderAsync(new XPayRefundOrderRequest
            {
                AccessToken = accessToken, OpenId = snapshot.OpenId, Environment = purchase.Env,
                PayOrderId = order.OutTradeNo, RefundOrderId = refund.RefundOrderId,
                LeftFee = refund.AmountInFen, RefundFee = refund.AmountInFen,
                BusinessMeta = metadata, RefundReason = "5", RefundFrom = "1"
            }, cancellationToken);
            using var raw = JsonDocument.Parse(response.GetRawBytes());
            if (!raw.RootElement.TryGetProperty("errcode", out var code) || !code.TryGetInt32(out _))
                throw new InvalidOperationException("The refund submission outcome is unconfirmed.");
            if (!response.IsSuccessful()) return new(false, response.ErrorCode, null);
            if (response.PayOrderId != order.OutTradeNo || response.PayWxOrderId != snapshot.PlatformOrderId ||
                response.RefundOrderId != refund.RefundOrderId || string.IsNullOrWhiteSpace(response.RefundWxOrderId) ||
                response.RefundWxOrderId.Length > 128)
                throw new InvalidOperationException("The refund acceptance does not match this purchase.");
            return new(true, null, response.RefundWxOrderId);
        }
        catch (CommonException)
        {
            // A timeout can happen AFTER acceptance. Never retry or retain a URL/token.
            throw new InvalidOperationException("The refund submission outcome is unconfirmed.");
        }
    }

    public async Task<VirtualRefundObservation> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        VirtualPaymentRefundRequest refund, string accessToken, CancellationToken cancellationToken = default)
    {
        var (purchase, metadata) = Validate(order, snapshot, refund);
        using var client = Client(snapshot, purchase.Env);
        XPayQueryOrderResponse response;
        try
        {
            response = await client.ExecuteXPayQueryOrderAsync(new XPayQueryOrderRequest
            {
                AccessToken = accessToken, OpenId = snapshot.OpenId, Environment = purchase.Env,
                OrderId = refund.RefundOrderId
            }, cancellationToken);
        }
        catch (CommonException) { throw new InvalidOperationException("The refund query is unavailable."); }
        if (!response.IsSuccessful()) throw new VirtualPaymentGatewayException(response.ErrorCode);
        // A purchase's broad status 5 is not refund-completion evidence. Query the
        // exact refund intent, require its metadata and explicit typed fields.
        using var raw = JsonDocument.Parse(response.GetRawBytes());
        var value = raw.RootElement.GetProperty("order");
        var id = value.GetProperty("wx_order_id").GetString();
        if (value.GetProperty("order_id").GetString() != refund.RefundOrderId ||
            value.GetProperty("order_type").GetInt32() != 1 ||
            value.GetProperty("env_type").GetInt32() != purchase.Env + 1 ||
            value.GetProperty("biz_type").GetInt32() is not (0 or 1000) ||
            value.GetProperty("refund_fee").GetInt32() != refund.AmountInFen ||
            value.GetProperty("biz_meta").GetString() != metadata ||
            string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
            refund.PlatformRefundId != null && refund.PlatformRefundId != id)
            throw new InvalidOperationException("The queried refund does not match this intent.");
        var status = value.GetProperty("status").GetInt32();
        return new(status == 8 ? VirtualRefundState.Completed : status == 7 ? VirtualRefundState.Failed : VirtualRefundState.Pending, id);
    }

    private (VirtualPaymentPurchase Purchase, string Metadata) Validate(PaymentOrder order, VirtualPaymentSnapshot snapshot, VirtualPaymentRefundRequest refund)
    {
        if (order.Channel != PaymentChannel.WeChatVirtual || refund.PaymentOrderId != order.Id ||
            !System.Text.RegularExpressions.Regex.IsMatch(refund.RefundOrderId, "^[A-Za-z0-9_-]{8,32}$") ||
            refund.AmountInFen != order.AmountInFen || refund.AmountInFen <= 0 ||
            string.IsNullOrWhiteSpace(snapshot.PlatformOrderId))
            throw new InvalidOperationException("The refund intent is not bound to a confirmed purchase.");
        return (VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId), Metadata(refund));
    }

    private RefundClient Client(VirtualPaymentSnapshot snapshot, int environment)
    {
        var settings = options.Value.WeChat.VirtualPayment;
        var key = environment == 0 ? settings.AppKey : settings.SandboxAppKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("The payment key is unavailable.");
        return new(new WechatApiClientOptions { AppId = snapshot.AppId, VirtualPaymentAppKey = key },
            clients.CreateClient(nameof(VirtualPaymentRefundGateway)));
    }

    public static string Metadata(VirtualPaymentRefundRequest refund) => JsonSerializer.Serialize(new
    { refund.PaymentOrderId, refund.RefundOrderId, refund.AmountInFen });

    private sealed class RefundClient(WechatApiClientOptions settings, HttpClient http) : WechatApiClient(settings, http);
}

public sealed record VirtualRefundSubmission(bool Accepted, int? ErrorCode, string? PlatformRefundId);
public sealed record VirtualRefundObservation(VirtualRefundState State, string PlatformRefundId);
public enum VirtualRefundState { Pending, Failed, Completed }
