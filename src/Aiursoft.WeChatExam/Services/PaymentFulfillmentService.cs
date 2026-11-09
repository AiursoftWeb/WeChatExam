using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Applies a confirmed payment to the shared membership, coupon and accounting records.
/// Gateway authentication, amount verification and reconciliation remain the caller's job.
/// </summary>
public class PaymentFulfillmentService(
    WeChatExamDbContext dbContext,
    ICouponService couponService,
    ChangeService changeService,
    ILogger<PaymentFulfillmentService> logger) : IScopedDependency
{
    /// <summary>
    /// Call only after verifying payment (or a zero-amount purchase), inside the
    /// caller's transaction and buyer/coupon/order locks. The caller commits;
    /// this service never contacts a gateway or treats a client response as proof.
    /// </summary>
    public async Task CompleteAsync(PaymentOrder order, string? transactionId)
    {
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Payment fulfillment requires the caller's transaction.");

        // The caller reloads the order under its locks before reaching this guard.
        // Repeated notifications or reconciliation must not extend VIP or consume a coupon twice.
        if (order.Status is PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded) return;

        order.Status = PaymentOrderStatus.Paid;
        order.WechatTransactionId = transactionId;
        order.PaidAt = DateTime.UtcNow;
        await ProcessVipActivationAsync(order);
        dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "StatusChanged", RawData = JsonSerializer.Serialize(new { newStatus = "Paid", transactionId }) });
        // Coupon/accounting services also save internally, so all saves stay in
        // the caller's transaction and are rolled back together if any step fails.
        await dbContext.SaveChangesAsync();
    }

    private async Task ProcessVipActivationAsync(PaymentOrder order)
    {
        if (order.VipProductId == null)
            return;

        var vipProduct = await dbContext.VipProducts.FindAsync(order.VipProductId);
        if (vipProduct == null)
            return;

        var durationDays = vipProduct.DurationDays;
        var now = DateTime.UtcNow;

        var existingVip = await dbContext.VipMemberships
            .FirstOrDefaultAsync(v => v.UserId == order.UserId && v.VipProductId == order.VipProductId);

        if (existingVip == null)
        {
            // New VIP membership for this product
            var vip = new VipMembership
            {
                UserId = order.UserId,
                VipProductId = order.VipProductId.Value,
                StartTime = now,
                EndTime = now.AddDays(durationDays),
                LastPaymentOrderId = order.Id
            };
            dbContext.VipMemberships.Add(vip);

            logger.LogInformation("Activated VIP {ProductName} for user {UserId}, expires {EndTime}",
                vipProduct.Name, order.UserId, vip.EndTime);
        }
        else
        {
            // Re-purchase: re-activate and extend from existing end time if still valid
            existingVip.EndTime = Math.Max(now.Ticks, existingVip.EndTime.Ticks) == existingVip.EndTime.Ticks
                ? existingVip.EndTime.AddDays(durationDays)
                : now.AddDays(durationDays);
                
            existingVip.StartTime = now;
            existingVip.LastPaymentOrderId = order.Id;

            logger.LogInformation("Re-activated VIP {ProductName} for user {UserId}, new expiry {EndTime}",
                vipProduct.Name, order.UserId, existingVip.EndTime);
        }

        // Record coupon usage if applicable
        if (order.CouponId.HasValue)
        {
            await couponService.RecordUsageAsync(order.CouponId.Value, order.UserId, order.Id, order.DiscountInFen);
        }

        // Preserve the same accounting event for either payment channel.
        var changeType = (order.AmountInFen == 0 && order.CouponId.HasValue) 
            ? ChangeType.VipActivatedViaCoupon 
            : ChangeType.VipActivatedViaPayment;

        await changeService.RecordChange(
            type: changeType,
            targetUserId: order.UserId,
            triggerUserId: order.UserId, // User triggered their own activation
            vipProductId: order.VipProductId,
            couponId: order.CouponId,
            details: $"Order: {order.OutTradeNo}. Amount: {order.AmountInFen / 100m}元. Discount: {order.DiscountInFen / 100m}元."
        );
    }
}
