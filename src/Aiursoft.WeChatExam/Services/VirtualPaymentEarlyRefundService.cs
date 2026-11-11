using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Internal settlement for an authenticated full refund received before local
/// fulfillment. The caller must query the original payment on the server, then
/// revalidate the immutable purchase under buyer/coupon/order locks in its own
/// transaction. This service neither queries WeChat nor initiates a refund.
/// </summary>
public class VirtualPaymentEarlyRefundService(WeChatExamDbContext db, ICouponService coupons) : IScopedDependency
{
    public async Task<VirtualPaymentRefundOutcome> CompleteAsync(PaymentOrder order,
        VirtualPaymentQueryResult originalPayment)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction == null ||
            db.Entry(order).State != EntityState.Unchanged || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Early refund settlement requires a clean tracked order and the caller's transaction and locks.");

        // Refund/reversal query status alone never proves a successful refund.
        // Only the authenticated, durably saved notification supplies that fact.
        var logs = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id).ToListAsync();
        var reviews = logs.Where(l => l.EventType == "VirtualPaymentRefundReview").ToList();
        if (reviews.Count != 1 || !TryReadReview(reviews[0], out var confirmation) || confirmation == null ||
            order.Channel != PaymentChannel.WeChatVirtual || order.AmountInFen <= 0 ||
            confirmation.PaymentOrderId != order.Id || confirmation.AmountInFen != order.AmountInFen ||
            originalPayment.PaymentOrderId != order.Id || originalPayment.AmountInFen != order.AmountInFen ||
            originalPayment.State != VirtualPaymentOrderState.RefundOrReversal ||
            originalPayment.PlatformOrderId != confirmation.PlatformOrderId)
            return VirtualPaymentRefundOutcome.ReviewRequired;

        var completed = logs.Where(l => l.EventType == "VirtualPaymentRefunded").ToList();
        if (completed.Count != 0)
            return completed.Count == 1 && order.Status == PaymentOrderStatus.Refunded && order.PaidAt == null &&
                   IsSameResolution(completed[0], reviews[0].Id, confirmation)
                ? VirtualPaymentRefundOutcome.Completed : VirtualPaymentRefundOutcome.ReviewRequired;

        // Never use this path to bypass the receipt-based reversal of a delivered
        // purchase. Gifts and other purchases are not modified, even for this VIP.
        if (order.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Failed) || order.PaidAt != null ||
            order.WechatTransactionId != null || logs.Any(l => l.EventType is "StatusChanged" or "VirtualPaymentConfirmed") ||
            await db.VipMemberships.AnyAsync(v => v.LastPaymentOrderId == order.Id) ||
            await db.CouponUsages.AnyAsync(u => u.PaymentOrderId == order.Id))
            return VirtualPaymentRefundOutcome.ReviewRequired;

        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id);
        if (snapshot == null || (snapshot.PlatformOrderId != null && snapshot.PlatformOrderId != confirmation.PlatformOrderId))
            return VirtualPaymentRefundOutcome.ReviewRequired;

        if (order.CouponId is { } couponId)
        {
            var coupon = await db.Coupons.SingleOrDefaultAsync(c => c.Id == couponId);
            if (coupon != null) await db.Entry(coupon).ReloadAsync();
            if (coupon == null || order.DiscountInFen <= 0 ||
                await db.UserClaimedCoupons.AnyAsync(c => c.CouponId == couponId && c.UserId == order.UserId && c.IsUsed) ||
                (coupon.IsSingleUse && (coupon.UsedByUserId != null ||
                    await db.CouponUsages.AnyAsync(u => u.CouponId == couponId) ||
                    await db.UserClaimedCoupons.AnyAsync(c => c.CouponId == couponId && c.UserId != order.UserId))))
                return VirtualPaymentRefundOutcome.ReviewRequired;
        }
        else if (order.DiscountInFen != 0)
            return VirtualPaymentRefundOutcome.ReviewRequired;

        if (snapshot.PlatformOrderId == null)
        {
            var updated = await db.VirtualPaymentSnapshots.Where(s => s.PaymentOrderId == order.Id && s.PlatformOrderId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlatformOrderId, confirmation.PlatformOrderId));
            if (updated != 1) throw new InvalidOperationException("The confirmed platform payment reference changed.");
        }
        // A paid-then-refunded purchase consumes its saved discount, but must not
        // fabricate a payment timestamp, a VIP activation or an accounting grant.
        // These writes and the terminal audit must commit/roll back together.
        if (order.CouponId is { } usedCoupon)
            await coupons.RecordUsageAsync(usedCoupon, order.UserId, order.Id, order.DiscountInFen);
        order.Status = PaymentOrderStatus.Refunded;
        db.PaymentLogs.Add(new PaymentLog
        {
            PaymentOrderId = order.Id, EventType = "VirtualPaymentRefunded",
            RawData = JsonSerializer.Serialize(new { confirmation, reviewId = reviews[0].Id,
                reason = "refunded_before_local_fulfillment", originalPayment })
        });
        await db.SaveChangesAsync();
        return VirtualPaymentRefundOutcome.Completed;
    }

    // Shared terminal proof for duplicate callbacks and administrator retries.
    // Refunded status alone is never enough to acknowledge a different refund.
    internal static async Task<bool> IsCompletedAsync(WeChatExamDbContext db, PaymentOrder order,
        VirtualPaymentRefundConfirmation? expected = null)
    {
        if (order.Channel != PaymentChannel.WeChatVirtual || order.Status != PaymentOrderStatus.Refunded ||
            order.PaidAt != null || order.AmountInFen <= 0) return false;
        var reviews = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "VirtualPaymentRefundReview").ToListAsync();
        if (reviews.Count != 1 || !TryReadReview(reviews[0], out var confirmation) || confirmation == null ||
            confirmation.PaymentOrderId != order.Id || confirmation.AmountInFen != order.AmountInFen ||
            (expected != null && confirmation != expected)) return false;
        var resolutions = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "VirtualPaymentRefunded").ToListAsync();
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id);
        return resolutions.Count == 1 && IsSameResolution(resolutions[0], reviews[0].Id, confirmation) &&
               snapshot?.PlatformOrderId == confirmation.PlatformOrderId;
    }

    internal static bool TryReadReview(PaymentLog log, out VirtualPaymentRefundConfirmation? confirmation)
    {
        confirmation = null;
        try
        {
            using var json = JsonDocument.Parse(log.RawData ?? "");
            if (json.RootElement.GetProperty("reason").GetString() != "payment_not_locally_settled") return false;
            confirmation = json.RootElement.GetProperty("confirmation").Deserialize<VirtualPaymentRefundConfirmation>();
            return confirmation != null && !string.IsNullOrWhiteSpace(confirmation.PlatformOrderId) &&
                   confirmation.PlatformOrderId.Length <= 128 && !string.IsNullOrWhiteSpace(confirmation.PlatformRefundId) &&
                   confirmation.PlatformRefundId.Length <= 128;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    internal static bool IsSameResolution(PaymentLog log, Guid reviewId, VirtualPaymentRefundConfirmation confirmation)
    {
        try
        {
            using var json = JsonDocument.Parse(log.RawData ?? "");
            return json.RootElement.GetProperty("reason").GetString() == "refunded_before_local_fulfillment" &&
                   json.RootElement.GetProperty("reviewId").GetGuid() == reviewId &&
                   json.RootElement.GetProperty("confirmation").Deserialize<VirtualPaymentRefundConfirmation>() == confirmation;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
    }
}
