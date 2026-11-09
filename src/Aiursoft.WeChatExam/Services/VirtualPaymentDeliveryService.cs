using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Server-side fallback when the normal delivery notification was not acknowledged.
/// Normal xpay_goods_deliver_notify replies do not need this API. This service
/// never grants rights: local fulfillment must already be committed before use.
/// See https://developers.weixin.qq.com/miniprogram/dev/server/API/VirtualPayment/api_notify_provide_goods.html
/// </summary>
public class VirtualPaymentDeliveryService(WeChatExamDbContext db, IVirtualPaymentQueryService query,
    VirtualPaymentAccessTokenService tokens, IHttpClientFactory httpClientFactory, IOptions<AppSettings> options,
    ILogger<VirtualPaymentDeliveryService> logger) : IScopedDependency
{
    public async Task<bool> ConfirmRecoveryAsync(string outTradeNo, string? userId, CancellationToken cancellationToken = default)
    {
        // An open transaction can expose uncommitted Paid state to this context.
        // Never tell WeChat that delivery finished before those rights are durable.
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(outTradeNo) ||
            !db.Database.IsRelational() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null) return false;
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.OutTradeNo == outTradeNo &&
            o.UserId == userId && o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);
        if (order is not { Status: PaymentOrderStatus.Paid, PaidAt: not null }) return false;
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
        if (snapshot == null) return false;

        try
        {
            var purchase = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
                try
                {
                    var observed = await query.QueryAsync(order, snapshot, token, cancellationToken);
                    if (observed.PaymentOrderId != order.Id || observed.AmountInFen != order.AmountInFen ||
                        string.IsNullOrWhiteSpace(observed.PlatformOrderId))
                        throw new InvalidOperationException("Virtual delivery observation does not match this purchase.");
                    if (!await StillPaidAsync(order, snapshot, cancellationToken)) return false;
                    if (observed.State == VirtualPaymentOrderState.Delivered) return true;
                    if (observed.State is not (VirtualPaymentOrderState.PaidAwaitingDelivery or VirtualPaymentOrderState.Delivering))
                        return false;
                    await NotifyAsync(order, purchase.Env, token, cancellationToken);
                    return true;
                }
                catch (VirtualPaymentGatewayException ex) when (ex.IsInvalidAccessToken && attempt == 0)
                {
                    // Requery with one ordinary reacquisition. Do not force-refresh,
                    // assume a failed HTTP response means failure, or grant twice.
                    await tokens.ForgetRejectedAsync(snapshot.AppId, token, cancellationToken);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            // Failure cannot undo already committed membership/coupon/accounting.
            // No SDK URL, raw response, session or token in diagnostics.
            logger.LogWarning("Virtual delivery recovery unavailable for {OutTradeNo}; code {Code}",
                order.OutTradeNo, (ex as VirtualPaymentGatewayException)?.ErrorCode);
        }
        return false;
    }

    private async Task<bool> StillPaidAsync(PaymentOrder original, VirtualPaymentSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null) return false;
        var current = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == original.Id, cancellationToken);
        var saved = await db.VirtualPaymentSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PaymentOrderId == original.Id, cancellationToken);
        return current is { Status: PaymentOrderStatus.Paid, PaidAt: not null, Channel: PaymentChannel.WeChatVirtual } &&
            current.UserId == original.UserId && current.OutTradeNo == original.OutTradeNo && current.AmountInFen == original.AmountInFen &&
            current.DiscountInFen == original.DiscountInFen && current.VipProductId == original.VipProductId &&
            current.CouponId == original.CouponId && saved != null && saved.AppId == snapshot.AppId &&
            saved.OpenId == snapshot.OpenId && saved.SignData == snapshot.SignData;
    }

    private async Task NotifyAsync(PaymentOrder order, int environment, string token, CancellationToken cancellationToken)
    {
        var settings = options.Value.WeChat;
        var key = environment == 1 ? settings.VirtualPayment.SandboxAppKey : settings.VirtualPayment.AppKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Virtual delivery configuration is incomplete.");
        using var client = new DeliveryClient(new WechatApiClientOptions { AppId = settings.AppId, VirtualPaymentAppKey = key },
            httpClientFactory.CreateClient(nameof(VirtualPaymentDeliveryService)));
        XPayNotifyProvideGoodsResponse response;
        try
        {
            // Merchant order_id OR wx_order_id, never both. The SDK signs the
            // actual serialized body; use the purchase's saved environment.
            response = await client.ExecuteXPayNotifyProvideGoodsAsync(new XPayNotifyProvideGoodsRequest
            { AccessToken = token, OrderId = order.OutTradeNo, Environment = environment }, cancellationToken);
        }
        catch (CommonException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (CommonException)
        {
            throw new InvalidOperationException("Virtual delivery confirmation is unavailable.");
        }
        if (!response.IsSuccessful()) throw new VirtualPaymentGatewayException(response.ErrorCode);
        // Unlike query_order, this response has no business fields to validate.
        // The SDK can leave default error code 0 for an invalid body; require
        // an explicit protocol acknowledgement rather than assuming success.
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(response.GetRawBytes());
            if (json.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("errcode", out var code) || !code.TryGetInt32(out var value) || value != 0)
                throw new InvalidOperationException("Virtual delivery acknowledgement is invalid.");
        }
        catch (System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Virtual delivery acknowledgement is invalid.");
        }
    }

    private sealed class DeliveryClient(WechatApiClientOptions settings, HttpClient httpClient) : WechatApiClient(settings, httpClient);
}
