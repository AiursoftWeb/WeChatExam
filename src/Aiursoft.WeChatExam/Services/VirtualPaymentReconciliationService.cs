using System.Data;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Recover an owned virtual purchase using SERVER verification only. This is
/// not checkout, a callback, delivery acknowledgement or a refund processor.
/// </summary>
public class VirtualPaymentReconciliationService(WeChatExamDbContext db, IVirtualPaymentQueryService query,
    VirtualPaymentAccessTokenService tokens, PaymentFulfillmentService fulfillment, IOptions<AppSettings> options,
    ILogger<VirtualPaymentReconciliationService> logger) : IScopedDependency
{
    public async Task<PaymentOrder?> ReconcileAsync(string outTradeNo, string? userId,
        CancellationToken cancellationToken = default)
    {
        // Authorize before contacting WeChat or allowing ANY financial effect.
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(outTradeNo)) return null;
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o =>
            o.OutTradeNo == outTradeNo && o.UserId == userId && o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);
        if (order == null || order.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Failed)) return order;
        if (!db.Database.IsRelational())
            throw new InvalidOperationException("Virtual payment reconciliation requires a relational database.");
        if (await db.PaymentLogs.AsNoTracking().AnyAsync(l => l.PaymentOrderId == order.Id &&
                (l.EventType == "VirtualPaymentRefundReview" || l.EventType == "VirtualPaymentRefunded"), cancellationToken)) return order;
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
        if (snapshot == null) return order;

        VirtualPaymentQueryResult result;
        try
        {
            _ = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            // Network operations stay OUTSIDE the financial transaction/locks.
            result = await QueryWithTokenAsync(order, snapshot, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Avoid logging an SDK URL, raw response or buyer session. Gateway
            // unavailability leaves the reservation intact for a later retry.
            logger.LogWarning("Virtual payment query unavailable for {OutTradeNo}; code {Code}",
                order.OutTradeNo, (ex as VirtualPaymentGatewayException)?.ErrorCode);
            return order;
        }
        if (result.PaymentOrderId != order.Id || result.AmountInFen != order.AmountInFen ||
            string.IsNullOrWhiteSpace(result.PlatformOrderId) || result.PlatformOrderId.Length > 128)
            throw new InvalidOperationException("The virtual payment result does not match this order.");
        if (result.State is not (VirtualPaymentOrderState.PaidAwaitingDelivery or VirtualPaymentOrderState.Delivering or
            VirtualPaymentOrderState.Delivered or VirtualPaymentOrderState.Closed)) return order;

        var retrying = false;
        // Once verified, finish persistence even if the phone disconnects. A
        // database failure still rolls ALL financial effects back together.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) db.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            // Same cross-process lock order as ordinary checkout/notifications.
            await db.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ConcurrencyStamp, x => x.ConcurrencyStamp));
            var current = await db.PaymentOrders.SingleAsync(o => o.Id == order.Id && o.UserId == userId);
            if (current.CouponId.HasValue)
                await db.Coupons.Where(c => c.Id == current.CouponId)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsEnabled, x => x.IsEnabled));
            await db.PaymentOrders.Where(o => o.Id == current.Id)
                .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, x => x.Status));
            await db.Entry(current).ReloadAsync();
            if (current.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Failed)) return current;
            // A refund can commit while the remote payment query is in flight.
            // Do not grant rights OR close/release its coupon reservation until
            // that refund is reconciled; a paid/closed query cannot erase it.
            if (await db.PaymentLogs.AsNoTracking().AnyAsync(l => l.PaymentOrderId == current.Id &&
                    (l.EventType == "VirtualPaymentRefundReview" || l.EventType == "VirtualPaymentRefunded"))) return current;

            var saved = await db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync(s => s.PaymentOrderId == current.Id);
            // A response obtained before waiting for locks cannot settle a
            // changed purchase, buyer, coupon or snapshot after acquiring them.
            if (current.Channel != PaymentChannel.WeChatVirtual || current.OutTradeNo != order.OutTradeNo ||
                current.AmountInFen != order.AmountInFen || current.DiscountInFen != order.DiscountInFen ||
                current.CouponId != order.CouponId || current.VipProductId != order.VipProductId ||
                saved.AppId != snapshot.AppId || saved.OpenId != snapshot.OpenId || saved.SignData != snapshot.SignData)
                throw new InvalidOperationException("The virtual purchase changed during reconciliation.");
            _ = VirtualPaymentPurchase.Read(current, saved, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            if (result.State == VirtualPaymentOrderState.Closed)
            {
                // Only authenticated server proof can release this reservation.
                // A phone cancellation or local deadline cannot close a payable
                // WeChat order. Never consume/restore a coupon or grant rights here.
                current.Status = PaymentOrderStatus.Closed;
                db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = current.Id, EventType = "VirtualPaymentClosed",
                    RawData = JsonSerializer.Serialize(new { current.OutTradeNo, result.PlatformOrderId }) });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
                return current;
            }
            if (!current.VipProductId.HasValue || !await db.VipProducts.AnyAsync(p => p.Id == current.VipProductId))
                throw new InvalidOperationException("The virtual payment product is unavailable.");

            // Commit the lookup reference with the original proof and grant.
            // A contradictory existing reference must never be overwritten.
            if (saved.PlatformOrderId != null && saved.PlatformOrderId != result.PlatformOrderId)
                throw new InvalidOperationException("The confirmed platform payment reference changed.");
            if (saved.PlatformOrderId == null)
            {
                var references = await db.VirtualPaymentSnapshots.Where(s => s.PaymentOrderId == current.Id && s.PlatformOrderId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PlatformOrderId, result.PlatformOrderId));
                if (references != 1)
                    throw new InvalidOperationException("The confirmed platform payment reference changed.");
            }

            // iOS need not have an ordinary WeChat transaction ID. Retain the
            // virtual platform reference separately; never fabricate that ID.
            db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = current.Id, EventType = "VirtualPaymentConfirmed",
                RawData = JsonSerializer.Serialize(new { current.OutTradeNo, result.PlatformOrderId, result.TransactionId }) });
            await fulfillment.CompleteAsync(current, result.TransactionId);
            await transaction.CommitAsync();
            return current;
        });
    }

    private async Task<VirtualPaymentQueryResult> QueryWithTokenAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
        try
        {
            return await query.QueryAsync(order, snapshot, token, cancellationToken);
        }
        catch (VirtualPaymentGatewayException ex) when (ex.IsInvalidAccessToken)
        {
            // Reacquire ONCE in ordinary mode. Never force-refresh or turn an
            // unavailable virtual order into an ordinary JSAPI payment.
            await tokens.ForgetRejectedAsync(snapshot.AppId, token, cancellationToken);
            token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
            return await query.QueryAsync(order, snapshot, token, cancellationToken);
        }
    }
}
