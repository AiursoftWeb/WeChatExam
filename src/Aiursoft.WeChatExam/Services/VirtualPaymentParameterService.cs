using System.Text.Json;
using System.Text.Json.Serialization;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Captures a new purchase and signs its saved snapshot. Neither operation
/// contacts a gateway, confirms payment or grants membership.
/// </summary>
public class VirtualPaymentParameterService(IOptions<AppSettings> options) : IScopedDependency
{
    private static readonly JsonSerializerOptions SigningJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// For a NEW reservation only. The caller must commit this snapshot together
    /// with its order before exposing payment parameters. Never recapture an old
    /// order from today's catalog; load its existing snapshot instead.
    /// </summary>
    public VirtualPaymentSnapshot Capture(PaymentOrder order, User buyer)
    {
        var settings = GetSettings();
        if (settings.Environment is not (0 or 1) || string.IsNullOrWhiteSpace(settings.OfferId))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");
        _ = GetAppKey(settings.Environment);
        var (openId, _) = ValidateOrderAndGetSession(order, buyer);
        if (order.VipProductId is not { } productId || !settings.Products.TryGetValue(productId.ToString("D"), out var product) ||
            string.IsNullOrWhiteSpace(product.ProductId) || product.PriceInFen <= 0)
            throw new InvalidOperationException("This product is unavailable for virtual payment.");
        if ((long)order.AmountInFen + order.DiscountInFen != product.PriceInFen)
            throw new InvalidOperationException("The saved order price does not match the virtual product.");

        var signData = JsonSerializer.Serialize(new Purchase
        {
            OfferId = settings.OfferId, BuyQuantity = 1, Env = settings.Environment, CurrencyType = "CNY",
            ProductId = product.ProductId, GoodsPrice = product.PriceInFen,
            ActivitySellingPrice = order.DiscountInFen > 0 ? order.AmountInFen : null,
            OutTradeNo = order.OutTradeNo, Attach = order.Id.ToString("N")
        }, SigningJson);
        if (options.Value.WeChat.AppId.Length > 64 || openId.Length > 128 || signData.Length > 4096)
            throw new InvalidOperationException("Virtual payment configuration exceeds supported limits.");
        return new VirtualPaymentSnapshot
        {
            PaymentOrderId = order.Id, AppId = options.Value.WeChat.AppId,
            OpenId = openId, SignData = signData
        };
    }

    /// <summary>
    /// Signs the exact stored bytes with the order's environment key and the
    /// buyer's current session_key. This is not permission to reuse a WeChat order
    /// number: the checkout caller must first reconcile any previous attempt.
    /// </summary>
    public VirtualPaymentParameters Prepare(PaymentOrder order, User buyer, VirtualPaymentSnapshot snapshot)
    {
        var settings = GetSettings();
        var (openId, sessionKey) = ValidateOrderAndGetSession(order, buyer);
        if (snapshot.PaymentOrderId != order.Id || snapshot.AppId != options.Value.WeChat.AppId ||
            snapshot.OpenId != openId || snapshot.SignData.Length > 4096)
            throw new InvalidOperationException("The virtual payment snapshot does not match this purchase.");
        Purchase? purchase;
        try
        {
            purchase = JsonSerializer.Deserialize<Purchase>(snapshot.SignData, SigningJson);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The virtual payment snapshot is invalid.");
        }
        if (purchase == null || purchase.Env is not (0 or 1) || purchase.OfferId != settings.OfferId ||
            string.IsNullOrWhiteSpace(purchase.OfferId) || purchase.BuyQuantity != 1 || purchase.CurrencyType != "CNY" ||
            string.IsNullOrWhiteSpace(purchase.ProductId) || purchase.OutTradeNo != order.OutTradeNo || purchase.Attach != order.Id.ToString("N") ||
            purchase.GoodsPrice != (long)order.AmountInFen + order.DiscountInFen ||
            (purchase.ActivitySellingPrice ?? purchase.GoodsPrice) != order.AmountInFen)
            throw new InvalidOperationException("The virtual payment snapshot does not match this purchase.");
        return new VirtualPaymentParameters
        {
            SignData = snapshot.SignData,
            PaySig = VirtualPaymentSignatures.SignPayment("requestVirtualPayment", snapshot.SignData, GetAppKey(purchase.Env)),
            Signature = VirtualPaymentSignatures.SignUser(snapshot.SignData, sessionKey)
        };
    }

    private VirtualPaymentSettings GetSettings()
    {
        if (!options.Value.WeChatEnabled || !options.Value.WeChat.VirtualPayment.Enabled)
            throw new InvalidOperationException("Virtual payment is not enabled.");
        if (string.IsNullOrWhiteSpace(options.Value.WeChat.AppId))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");
        return options.Value.WeChat.VirtualPayment;
    }

    private string GetAppKey(int environment)
    {
        var settings = options.Value.WeChat.VirtualPayment;
        var key = environment == 1 ? settings.SandboxAppKey : settings.AppKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Virtual payment configuration is incomplete.");
        return key;
    }

    private static (string OpenId, string SessionKey) ValidateOrderAndGetSession(PaymentOrder order, User buyer)
    {
        // The caller loads the authenticated buyer and refreshes code2Session.
        // A stored session_key alone is not proof that the login is still fresh.
        var openId = buyer.MiniProgramOpenId;
        var sessionKey = buyer.SessionKey;
        if (order.UserId != buyer.Id || string.IsNullOrWhiteSpace(openId) || string.IsNullOrWhiteSpace(sessionKey))
            throw new InvalidOperationException("Please sign in again before paying.");
        if (order.Channel != PaymentChannel.WeChatVirtual || order.Status != PaymentOrderStatus.Pending ||
            order.ExpiredAt <= DateTime.UtcNow || !IsValidOrderNumber(order.OutTradeNo))
            throw new InvalidOperationException("This order is unavailable for virtual payment.");
        // Fully discounted purchases use local fulfillment, not a gateway request.
        if (order.AmountInFen <= 0 || order.DiscountInFen < 0)
            throw new InvalidOperationException("This amount cannot be sent to virtual payment.");
        return (openId, sessionKey);
    }

    private static bool IsValidOrderNumber(string value) => value.Length is >= 8 and <= 32 && value[0] != '_' &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || "_-|*@".Contains(c));

    private sealed class Purchase
    {
        public string OfferId { get; init; } = string.Empty;
        public int BuyQuantity { get; init; }
        public int Env { get; init; }
        public string CurrencyType { get; init; } = string.Empty;
        public string ProductId { get; init; } = string.Empty;
        public int GoodsPrice { get; init; }
        public int? ActivitySellingPrice { get; init; }
        public string OutTradeNo { get; init; } = string.Empty;
        public string Attach { get; init; } = string.Empty;
    }
}
