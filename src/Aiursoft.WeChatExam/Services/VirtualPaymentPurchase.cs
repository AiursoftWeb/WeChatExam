using System.Text.Json;
using System.Text.Json.Serialization;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// The saved purchase, independent of today's catalog, sales switch and login.
/// Client signing and server reconciliation must interpret the same snapshot.
/// </summary>
internal sealed class VirtualPaymentPurchase
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [JsonRequired]
    public string OfferId { get; init; } = string.Empty;
    [JsonRequired]
    public int BuyQuantity { get; init; }
    [JsonRequired]
    public int Env { get; init; }
    [JsonRequired]
    public string CurrencyType { get; init; } = string.Empty;
    [JsonRequired]
    public string ProductId { get; init; } = string.Empty;
    [JsonRequired]
    public int GoodsPrice { get; init; }
    public int? ActivitySellingPrice { get; init; }
    [JsonRequired]
    public string OutTradeNo { get; init; } = string.Empty;
    [JsonRequired]
    public string Attach { get; init; } = string.Empty;

    public static VirtualPaymentPurchase Read(PaymentOrder order, VirtualPaymentSnapshot snapshot, string appId, string offerId)
    {
        // Expiration and the sales switch control NEW checkout only. A paid
        // order may need recovery after expiration, logout or disabling sales.
        if (order.Channel != PaymentChannel.WeChatVirtual || order.AmountInFen <= 0 || order.DiscountInFen < 0 ||
            snapshot.PaymentOrderId != order.Id || string.IsNullOrWhiteSpace(appId) || snapshot.AppId != appId ||
            string.IsNullOrWhiteSpace(snapshot.OpenId) || snapshot.OpenId.Length > 128 ||
            string.IsNullOrWhiteSpace(snapshot.SignData) || snapshot.SignData.Length > 4096)
            throw new InvalidOperationException("The virtual payment snapshot does not match this purchase.");

        VirtualPaymentPurchase? purchase;
        try
        {
            purchase = JsonSerializer.Deserialize<VirtualPaymentPurchase>(snapshot.SignData, Json);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("The virtual payment snapshot is invalid.");
        }
        if (purchase == null || purchase.Env is not (0 or 1) || purchase.OfferId != offerId ||
            string.IsNullOrWhiteSpace(purchase.OfferId) || purchase.BuyQuantity != 1 || purchase.CurrencyType != "CNY" ||
            string.IsNullOrWhiteSpace(purchase.ProductId) || purchase.OutTradeNo != order.OutTradeNo ||
            purchase.Attach != order.Id.ToString("N") ||
            purchase.GoodsPrice != (long)order.AmountInFen + order.DiscountInFen ||
            (purchase.ActivitySellingPrice ?? purchase.GoodsPrice) != order.AmountInFen)
            throw new InvalidOperationException("The virtual payment snapshot does not match this purchase.");
        return purchase;
    }
}
