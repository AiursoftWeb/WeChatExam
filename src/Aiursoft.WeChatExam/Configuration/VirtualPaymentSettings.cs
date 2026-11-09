namespace Aiursoft.WeChatExam.Configuration;

public class VirtualPaymentSettings
{
    public bool Enabled { get; init; }

    // WeChat uses 0 for live and 1 for sandbox. Never infer this from the
    // client's platform or fall back to a key belonging to another environment.
    public int Environment { get; init; } = 1;
    public string OfferId { get; init; } = string.Empty;
    public string AppKey { get; init; } = string.Empty;
    public string SandboxAppKey { get; init; } = string.Empty;

    // Server-managed mapping from our VIP product to the published WeChat goods.
    // The price is the published full price in fen, before our order's discount.
    // ConfigurationBinder supports string dictionary keys, not Guid keys.
    // Use the VIP product's dashed GUID; case does not change its identity.
    public Dictionary<string, VirtualPaymentProductSettings> Products { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public class VirtualPaymentProductSettings
{
    public string ProductId { get; init; } = string.Empty;
    public int PriceInFen { get; init; }
}
