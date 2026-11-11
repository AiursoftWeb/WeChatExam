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
        var entitlement = await ProcessVipActivationAsync(order);
        // Preserve the ordinary JSAPI log contract. New virtual grants also keep
        // the exact before/after entitlement, so a refund need not guess from
        // today's product duration or remove unrelated purchases and gifts.
        var rawData = entitlement == null
            ? JsonSerializer.Serialize(new { newStatus = "Paid", transactionId })
            : JsonSerializer.Serialize(new { newStatus = "Paid", transactionId, entitlement });
        dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "StatusChanged", RawData = rawData });
        // Coupon/accounting services also save internally, so all saves stay in
        // the caller's transaction and are rolled back together if any step fails.
        await dbContext.SaveChangesAsync();
    }

    private async Task<VirtualPaymentEntitlementReceipt?> ProcessVipActivationAsync(PaymentOrder order)
    {
        if (order.VipProductId == null)
        {
            if (order.Channel == PaymentChannel.WeChatVirtual)
                throw new InvalidOperationException("A virtual payment requires an entitlement target.");
            return null;
        }

        var vipProduct = await dbContext.VipProducts.FindAsync(order.VipProductId);
        if (vipProduct == null)
        {
            if (order.Channel == PaymentChannel.WeChatVirtual)
                throw new InvalidOperationException("The virtual payment entitlement target is unavailable.");
            return null;
        }

        var durationDays = vipProduct.DurationDays;
        if (order.Channel == PaymentChannel.WeChatVirtual)
        {
            var goodsId = await dbContext.VirtualPaymentSnapshots.Where(s => s.PaymentOrderId == order.Id)
                .Select(s => s.VirtualPaymentGoodsId).SingleOrDefaultAsync();
            if (goodsId != null)
            {
                var goods = await dbContext.VirtualPaymentGoods.SingleAsync(g => g.Id == goodsId);
                if (goods.SourceProductId != order.VipProductId || goods.DurationDays <= 0 ||
                    goods.PriceInFen != (long)order.AmountInFen + order.DiscountInFen)
                    throw new InvalidOperationException("The saved goods revision does not match this purchase.");
                durationDays = goods.DurationDays;
            }
        }
        var now = DateTime.UtcNow;

        var existingVip = await dbContext.VipMemberships
            .FirstOrDefaultAsync(v => v.UserId == order.UserId && v.VipProductId == order.VipProductId);

        var before = order.Channel == PaymentChannel.WeChatVirtual && existingVip != null
            ? new PaymentEntitlementState(existingVip.StartTime, existingVip.EndTime, existingVip.LastPaymentOrderId)
            : null;
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
            existingVip = vip;

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

        if (order.Channel != PaymentChannel.WeChatVirtual) return null;
        await dbContext.SaveChangesAsync();
        // Read back the persisted value, not tracked DateTime values: providers
        // can round timestamps (e.g. MySQL datetime(6)). A later refund must compare
        // against the actual stored grant, inside the same transaction and locks.
        var after = await dbContext.VipMemberships.AsNoTracking().Where(v => v.Id == existingVip.Id &&
                v.UserId == order.UserId && v.VipProductId == order.VipProductId)
            .Select(v => new PaymentEntitlementState(v.StartTime, v.EndTime, v.LastPaymentOrderId))
            .SingleAsync();
        return new VirtualPaymentEntitlementReceipt(1, existingVip.Id, order.UserId, vipProduct.Id, before, after);
    }
}
