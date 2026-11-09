using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using SKIT.FlurlHttpClient;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Reads a virtual order without changing local orders or granting membership.
/// The trusted caller supplies an access_token for the snapshot's AppId; this
/// is not a public API and must never accept a token or order from a client.
/// See https://developers.weixin.qq.com/miniprogram/dev/server/API/VirtualPayment/api_query_order.html
/// </summary>
public class VirtualPaymentQueryService(IOptions<AppSettings> options, IHttpClientFactory httpClientFactory) : IScopedDependency
{
    public async Task<VirtualPaymentQueryResult> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        string accessToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var weChat = options.Value.WeChat;
        var purchase = VirtualPaymentPurchase.Read(order, snapshot, weChat.AppId, weChat.VirtualPayment.OfferId);
        var key = purchase.Env == 1 ? weChat.VirtualPayment.SandboxAppKey : weChat.VirtualPayment.AppKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");

        using var client = new QueryClient(new WechatApiClientOptions
        {
            AppId = snapshot.AppId, VirtualPaymentAppKey = key
        }, httpClientFactory.CreateClient(nameof(VirtualPaymentQueryService)));
        XPayQueryOrderResponse response;
        try
        {
            // The SDK signs its own exact serialized body, with no user session
            // signature. Always query the saved buyer and purchase environment.
            response = await client.ExecuteXPayQueryOrderAsync(new XPayQueryOrderRequest
            {
                AccessToken = accessToken, OpenId = snapshot.OpenId,
                Environment = purchase.Env, OrderId = order.OutTradeNo
            }, cancellationToken);
        }
        catch (CommonException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (CommonException)
        {
            // SDK exceptions may contain the URL (including access_token).
            throw new InvalidOperationException("Virtual payment order query failed.");
        }
        if (!response.IsSuccessful())
            throw new InvalidOperationException($"Virtual payment order query failed (code {response.ErrorCode}).");

        var remote = response.Order;
        // Request env is 0/1; response env_type is 1/2. Refund orders (1/8)
        // are never evidence of a successful purchase, including on Apple iOS.
        if (remote == null || remote.OrderId != order.OutTradeNo || remote.Environment != purchase.Env + 1 ||
            remote.OrderType is not (0 or 7) || remote.BusinessType != 0 || remote.OrderFee != order.AmountInFen ||
            string.IsNullOrWhiteSpace(remote.WxOrderId) || remote.RefundFee is < 0 || remote.RefundFee > order.AmountInFen ||
            remote.LeftFee is < 0 || remote.LeftFee > order.AmountInFen || remote.PaidFee < 0)
            throw new InvalidOperationException("The virtual payment query does not match this purchase.");

        var state = remote.Status switch
        {
            0 => VirtualPaymentOrderState.Initializing,
            1 => VirtualPaymentOrderState.Created,
            2 => VirtualPaymentOrderState.PaidAwaitingDelivery,
            3 => VirtualPaymentOrderState.Delivering,
            4 => VirtualPaymentOrderState.Delivered,
            6 => VirtualPaymentOrderState.Closed,
            5 or 7 or 8 or 9 or 10 => VirtualPaymentOrderState.RefundOrReversal,
            _ => throw new InvalidOperationException("The virtual payment query returned an unknown state.")
        };
        // A subsidy can make paid_fee smaller than order_fee. A balance below
        // BOTH amounts is a refund signal; merely below order_fee is not.
        // Refund reconciliation remains separate and cannot reactivate rights.
        if ((remote.RefundFee is > 0 || remote.LeftFee < Math.Min(remote.PaidFee, order.AmountInFen)) &&
            state is VirtualPaymentOrderState.PaidAwaitingDelivery or
            VirtualPaymentOrderState.Delivering or VirtualPaymentOrderState.Delivered)
            state = VirtualPaymentOrderState.RefundOrReversal;

        // paid_fee may differ due to platform subsidies. order_fee must equal
        // the merchant's saved payable amount, not today's price or full price.
        return new VirtualPaymentQueryResult(state, remote.WxOrderId, remote.TransactionId);
    }

    private sealed class QueryClient(WechatApiClientOptions settings, HttpClient httpClient)
        : WechatApiClient(settings, httpClient);
}

public enum VirtualPaymentOrderState
{
    Initializing,
    Created,
    PaidAwaitingDelivery,
    Delivering,
    Delivered,
    Closed,
    RefundOrReversal
}

/// <summary>A validated observation, not a command to fulfill or close an order.</summary>
public sealed record VirtualPaymentQueryResult(VirtualPaymentOrderState State, string PlatformOrderId, string? TransactionId);
