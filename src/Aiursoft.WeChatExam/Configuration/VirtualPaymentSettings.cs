namespace Aiursoft.WeChatExam.Configuration;

public class VirtualPaymentSettings
{
    public bool Enabled { get; init; }
    public VirtualPaymentGoodsSyncSettings GoodsSync { get; init; } = new();

    // Opt in only after validating the iOS order-reference mapping and encrypted
    // HTTP reply with the platform. This is independent of accepting new sales.
    public bool IosRefundAdviceEnabled { get; init; }

    // WeChat uses 0 for live and 1 for sandbox. Never infer this from the
    // client's platform or fall back to a key belonging to another environment.
    public int Environment { get; init; } = 1;
    public string OfferId { get; init; } = string.Empty;
    public string AppKey { get; init; } = string.Empty;
    public string SandboxAppKey { get; init; } = string.Empty;

    // Mini-program message-push credentials, independent of Tenpay v3. Empty
    // defaults keep the new callback unavailable without affecting old clients.
    public string CallbackToken { get; init; } = string.Empty;
    public string CallbackEncodingAesKey { get; init; } = string.Empty;

    // Shared with the actual notification endpoint; diagnostics must not report
    // a callback as configured when the endpoint would reject its credentials.
    public bool HasValidCallbackCredentials(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(CallbackToken) ||
            CallbackToken.Length > 256 || CallbackEncodingAesKey.Length != 43) return false;
        try { return Convert.FromBase64String(CallbackEncodingAesKey + "=").Length == 32; }
        catch (FormatException) { return false; }
    }

    // Server-managed mapping from our VIP product to the published WeChat goods.
    // The price is the published full price in fen, before our order's discount.
    // ConfigurationBinder supports string dictionary keys, not Guid keys.
    // Use the VIP product's dashed GUID; case does not change its identity.
    public Dictionary<string, VirtualPaymentProductSettings> Products { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public class VirtualPaymentProductSettings
{
    public Guid? GoodsVersionId { get; init; }
    public string ProductId { get; init; } = string.Empty;
    public int PriceInFen { get; init; }
}

public class VirtualPaymentGoodsSyncSettings
{
    // Independent opt-in: goods can be prepared while accepting new payments is off.
    public bool Enabled { get; init; }
    public string PictureUrl { get; init; } = string.Empty;
}
