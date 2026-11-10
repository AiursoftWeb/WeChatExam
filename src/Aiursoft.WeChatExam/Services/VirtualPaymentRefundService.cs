using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Settles an already SERVER-verified cash refund, never initiates one. The caller
/// must authenticate the platform result and bind the original buyer/AppId/env,
/// then reload the order under buyer/coupon/order locks in its own transaction.
/// No HTTP endpoint or client callback may supply this confirmation directly.
/// </summary>
public class VirtualPaymentRefundService(WeChatExamDbContext db) : IScopedDependency
{
    public async Task<VirtualPaymentRefundOutcome> CompleteAsync(PaymentOrder order,
        VirtualPaymentRefundConfirmation confirmation)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction == null ||
            db.Entry(order).State == EntityState.Detached)
            throw new InvalidOperationException("Refund settlement requires a tracked order and the caller's transaction and locks.");
        if (order.Channel != PaymentChannel.WeChatVirtual || order.Id != confirmation.PaymentOrderId ||
            order.AmountInFen <= 0 || confirmation.AmountInFen <= 0 || confirmation.AmountInFen > order.AmountInFen ||
            string.IsNullOrWhiteSpace(confirmation.PlatformOrderId) || confirmation.PlatformOrderId.Length > 128 ||
            string.IsNullOrWhiteSpace(confirmation.PlatformRefundId) || confirmation.PlatformRefundId.Length > 128)
            throw new InvalidOperationException("The refund confirmation does not match this virtual purchase.");

        // Idempotence includes manual cases. A repeated refund ID may not change
        // its amount or original payment reference on a subsequent delivery.
        var prior = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            (l.EventType == "VirtualPaymentRefunded" || l.EventType == "VirtualPaymentRefundReview")).ToListAsync();
        foreach (var log in prior)
        {
            var saved = ReadConfirmation(log.RawData);
            if (saved?.PlatformRefundId != confirmation.PlatformRefundId) continue;
            if (saved != confirmation) throw new InvalidOperationException("The refund identity changed.");
            return log.EventType == "VirtualPaymentRefunded" && order.Status == PaymentOrderStatus.Refunded
                ? VirtualPaymentRefundOutcome.Completed : VirtualPaymentRefundOutcome.ReviewRequired;
        }
        if (order.Status != PaymentOrderStatus.Paid || order.PaidAt == null)
            return await ReviewAsync(order, confirmation, "payment_not_locally_settled");

        var paymentLogs = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "VirtualPaymentConfirmed").Select(l => l.RawData).ToListAsync();
        var platformOrder = paymentLogs.Count == 1 ? ReadPlatformOrder(paymentLogs[0], order.OutTradeNo) : null;
        if (platformOrder == null) return await ReviewAsync(order, confirmation, "original_payment_evidence_missing");
        if (platformOrder != confirmation.PlatformOrderId)
            throw new InvalidOperationException("The refund belongs to another platform payment.");
        // Partial refunds and platform-funded discounts need human reconciliation;
        // never guess a proportional entitlement from today's product duration.
        if (confirmation.AmountInFen != order.AmountInFen)
            return await ReviewAsync(order, confirmation, "partial_or_adjusted_refund");

        // The full cash refund is a financial fact even when its rights require
        // manual review. Late paid notifications must not re-grant this purchase.
        order.Status = PaymentOrderStatus.Refunded;
        var grants = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "StatusChanged").Select(l => l.RawData).ToListAsync();
        var receipt = grants.Count == 1 ? VirtualPaymentEntitlementReceipt.ReadGrant(grants[0]) : null;
        if (receipt == null || receipt.Version != 1 || receipt.UserId != order.UserId ||
            receipt.VipProductId != order.VipProductId || receipt.MembershipId == Guid.Empty ||
            receipt.After.LastPaymentOrderId != order.Id || receipt.After.EndTime <= receipt.After.StartTime ||
            receipt.Before?.LastPaymentOrderId == order.Id || receipt.Before?.EndTime >= receipt.After.EndTime)
            return await ReviewAsync(order, confirmation, "entitlement_evidence_missing_or_invalid");
        if (receipt.Before?.LastPaymentOrderId is { } previousOrder && !await db.PaymentOrders.AnyAsync(o =>
                o.Id == previousOrder && o.UserId == order.UserId && o.VipProductId == order.VipProductId && o.Status == PaymentOrderStatus.Paid))
            return await ReviewAsync(order, confirmation, "previous_entitlement_purchase_unavailable");
        if (db.ChangeTracker.Entries<VipMembership>().Any(e => e.Entity.Id == receipt.MembershipId && e.State != EntityState.Unchanged))
            throw new InvalidOperationException("Refund settlement cannot overwrite pending membership edits.");

        // Compare AND update in one SQL statement: a teacher may edit a membership
        // without payment locks. Never overwrite a gift, extension or later order.
        // A newly created membership becomes an empty interval; retain its audit row.
        var restored = receipt.Before ?? new PaymentEntitlementState(receipt.After.StartTime, receipt.After.StartTime, order.Id);
        var changed = await db.VipMemberships.Where(v => v.Id == receipt.MembershipId && v.UserId == order.UserId &&
                v.VipProductId == order.VipProductId && v.StartTime == receipt.After.StartTime &&
                v.EndTime == receipt.After.EndTime && v.LastPaymentOrderId == receipt.After.LastPaymentOrderId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.StartTime, restored.StartTime)
                .SetProperty(v => v.EndTime, restored.EndTime).SetProperty(v => v.LastPaymentOrderId, restored.LastPaymentOrderId));
        if (changed != 1) return await ReviewAsync(order, confirmation, "membership_changed_or_missing");
        // Coupons, claimed coupons, usage rows and historical purchase prices are
        // deliberately untouched: a spent single-use coupon stays spent.
        db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentRefunded",
            RawData = JsonSerializer.Serialize(new { confirmation, receipt.MembershipId }) });
        await db.SaveChangesAsync();
        return VirtualPaymentRefundOutcome.Completed;
    }

    private async Task<VirtualPaymentRefundOutcome> ReviewAsync(PaymentOrder order,
        VirtualPaymentRefundConfirmation confirmation, string reason)
    {
        db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentRefundReview",
            RawData = JsonSerializer.Serialize(new { confirmation, reason }) });
        await db.SaveChangesAsync();
        return VirtualPaymentRefundOutcome.ReviewRequired;
    }

    private static VirtualPaymentRefundConfirmation? ReadConfirmation(string? raw)
    {
        try
        {
            using var json = JsonDocument.Parse(raw ?? "");
            return json.RootElement.GetProperty("confirmation").Deserialize<VirtualPaymentRefundConfirmation>();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private static string? ReadPlatformOrder(string? raw, string outTradeNo)
    {
        try
        {
            using var json = JsonDocument.Parse(raw ?? "");
            return json.RootElement.GetProperty("OutTradeNo").GetString() == outTradeNo
                ? json.RootElement.GetProperty("PlatformOrderId").GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }
}

/// <summary>Internal evidence from the authenticated refund gateway, not a public request model.</summary>
public sealed record VirtualPaymentRefundConfirmation(Guid PaymentOrderId, string PlatformOrderId,
    string PlatformRefundId, int AmountInFen);

public enum VirtualPaymentRefundOutcome { Completed, ReviewRequired }
