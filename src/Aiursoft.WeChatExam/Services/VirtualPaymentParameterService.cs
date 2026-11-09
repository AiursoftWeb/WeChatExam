using System.Text.Json;
using System.Text.Json.Serialization;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Prepares client signatures for a server-reserved virtual order. This does not
/// create an order, contact a gateway, confirm payment or grant membership.
/// </summary>
public class VirtualPaymentParameterService(IOptions<AppSettings> options) : IScopedDependency
{
    private static readonly JsonSerializerOptions SigningJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public VirtualPaymentParameters Prepare(PaymentOrder order, User buyer)
    {
        var settings = options.Value.WeChat.VirtualPayment;
        if (!options.Value.WeChatEnabled || !settings.Enabled)
            throw new InvalidOperationException("Virtual payment is not enabled.");
        if (settings.Environment is not (0 or 1) || string.IsNullOrWhiteSpace(settings.OfferId) ||
            string.IsNullOrWhiteSpace(options.Value.WeChat.AppId))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");
        var appKey = settings.Environment == 1 ? settings.SandboxAppKey : settings.AppKey;
        if (string.IsNullOrWhiteSpace(appKey))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");

        // The caller must load this order for the authenticated buyer and refresh
        // code2Session when necessary. A stored session_key is not proof of freshness.
        if (order.UserId != buyer.Id || string.IsNullOrWhiteSpace(buyer.MiniProgramOpenId) || string.IsNullOrWhiteSpace(buyer.SessionKey))
            throw new InvalidOperationException("Please sign in again before paying.");
        if (order.Channel != PaymentChannel.WeChatVirtual || order.Status != PaymentOrderStatus.Pending ||
            order.ExpiredAt <= DateTime.UtcNow || !IsValidOrderNumber(order.OutTradeNo))
            throw new InvalidOperationException("This order is unavailable for virtual payment.");
        if (order.VipProductId is not { } productId || !settings.Products.TryGetValue(productId.ToString("D"), out var product) ||
            string.IsNullOrWhiteSpace(product.ProductId) || product.PriceInFen <= 0)
            throw new InvalidOperationException("This product is unavailable for virtual payment.");
        if (order.AmountInFen <= 0 || order.DiscountInFen < 0)
            throw new InvalidOperationException("This amount cannot be sent to virtual payment.");
        // Never recalculate an existing order from today's catalog or accept a
        // client-supplied price. A mapping mismatch requires reconciliation first.
        if ((long)order.AmountInFen + order.DiscountInFen != product.PriceInFen)
            throw new InvalidOperationException("The saved order price does not match the virtual product.");

        var signData = JsonSerializer.Serialize(new
        {
            offerId = settings.OfferId, buyQuantity = 1, env = settings.Environment, currencyType = "CNY",
            productId = product.ProductId, goodsPrice = product.PriceInFen,
            activitySellingPrice = order.DiscountInFen > 0 ? (int?)order.AmountInFen : null,
            outTradeNo = order.OutTradeNo, attach = order.Id.ToString("N")
        }, SigningJson);
        return new VirtualPaymentParameters
        {
            SignData = signData,
            PaySig = VirtualPaymentSignatures.SignPayment("requestVirtualPayment", signData, appKey),
            Signature = VirtualPaymentSignatures.SignUser(signData, buyer.SessionKey)
        };
    }

    private static bool IsValidOrderNumber(string value) => value.Length is >= 8 and <= 32 && value[0] != '_' &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || "_-|*@".Contains(c));
}
