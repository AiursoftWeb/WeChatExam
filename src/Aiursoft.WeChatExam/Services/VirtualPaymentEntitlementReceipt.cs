namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Versioned evidence stored in a virtual order's existing StatusChanged log,
/// atomically with its grant. Missing historical evidence must never be inferred
/// from current catalog settings. This record alone does not authorize a refund.
/// </summary>
public sealed record VirtualPaymentEntitlementReceipt(int Version, Guid MembershipId,
    string UserId, Guid VipProductId, PaymentEntitlementState? Before, PaymentEntitlementState After);

/// <summary>UTC entitlement values. A null Before means this purchase created the membership.</summary>
public sealed record PaymentEntitlementState(DateTime StartTime, DateTime EndTime, Guid? LastPaymentOrderId);
