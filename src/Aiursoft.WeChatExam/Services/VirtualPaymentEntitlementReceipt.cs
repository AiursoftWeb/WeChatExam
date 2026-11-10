using System.Text.Json;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Versioned evidence stored in a virtual order's existing StatusChanged log,
/// atomically with its grant. Missing historical evidence must never be inferred
/// from current catalog settings. This record alone does not authorize a refund.
/// </summary>
public sealed record VirtualPaymentEntitlementReceipt(int Version, Guid MembershipId,
    string UserId, Guid VipProductId, PaymentEntitlementState? Before, PaymentEntitlementState After)
{
    internal static VirtualPaymentEntitlementReceipt? ReadGrant(string? raw)
    {
        try
        {
            using var json = JsonDocument.Parse(raw ?? "");
            if (json.RootElement.GetProperty("newStatus").GetString() != "Paid") return null;
            var entitlement = json.RootElement.GetProperty("entitlement");
            // An omitted Before is NOT evidence that no membership existed.
            // Require the original schema, including explicit nulls, before relying on it.
            var before = entitlement.GetProperty("Before");
            var after = entitlement.GetProperty("After");
            if (before.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return null;
            foreach (var name in new[] { "StartTime", "EndTime", "LastPaymentOrderId" })
            {
                _ = after.GetProperty(name);
                if (before.ValueKind == JsonValueKind.Object) _ = before.GetProperty(name);
            }
            return entitlement.Deserialize<VirtualPaymentEntitlementReceipt>();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

/// <summary>UTC entitlement values. A null Before means this purchase created the membership.</summary>
public sealed record PaymentEntitlementState(DateTime StartTime, DateTime EndTime, Guid? LastPaymentOrderId);
