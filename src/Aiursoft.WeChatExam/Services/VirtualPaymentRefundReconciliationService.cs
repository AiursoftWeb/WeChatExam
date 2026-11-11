using System.Data;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Server-side verification of already received early refunds; never initiates a refund.</summary>
public class VirtualPaymentRefundReconciliationService(WeChatExamDbContext db, IVirtualPaymentQueryService query,
    VirtualPaymentAccessTokenService tokens, VirtualPaymentEarlyRefundService settlement, IOptions<AppSettings> options,
    ILogger<VirtualPaymentRefundReconciliationService> logger) : IScopedDependency
{
    public async Task<VirtualPaymentRefundReconciliationResult> ReconcileAsync(Guid orderId, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null ||
            System.Transactions.Transaction.Current != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Refund reconciliation requires its own relational transaction and no pending changes.");
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId &&
            o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);
        if (order == null) return VirtualPaymentRefundReconciliationResult.NotFound;
        if (await VirtualPaymentEarlyRefundService.IsCompletedAsync(db, order))
            return VirtualPaymentRefundReconciliationResult.Completed;
        if (order.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Failed))
            return VirtualPaymentRefundReconciliationResult.ReviewRequired;
        var reviews = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "VirtualPaymentRefundReview").ToListAsync(cancellationToken);
        if (reviews.Count != 1 || !VirtualPaymentEarlyRefundService.TryReadReview(reviews[0], out var refund) ||
            refund == null || refund.PaymentOrderId != order.Id || refund.AmountInFen != order.AmountInFen)
            return VirtualPaymentRefundReconciliationResult.ReviewRequired;
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
        if (snapshot == null) return VirtualPaymentRefundReconciliationResult.ReviewRequired;

        VirtualPaymentQueryResult result;
        try
        {
            _ = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            // No database transaction or financial locks during network I/O.
            var token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
            try { result = await query.QueryAsync(order, snapshot, token, cancellationToken); }
            catch (VirtualPaymentGatewayException ex) when (ex.IsInvalidAccessToken)
            {
                await tokens.ForgetRejectedAsync(snapshot.AppId, token, cancellationToken);
                token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
                result = await query.QueryAsync(order, snapshot, token, cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
            // Do not log SDK URLs, access tokens or untrusted response bodies.
            logger.LogWarning("Virtual refund query unavailable for order {OrderId}.", order.Id);
            return VirtualPaymentRefundReconciliationResult.Unavailable;
        }
        if (result.PaymentOrderId != order.Id || result.AmountInFen != order.AmountInFen ||
            result.State != VirtualPaymentOrderState.RefundOrReversal || result.PlatformOrderId != refund.PlatformOrderId)
            return VirtualPaymentRefundReconciliationResult.ReviewRequired;

        var retrying = false;
        // Once verified, persist even if the browser disconnects. All effects,
        // including the administrator audit, belong to this single transaction.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) db.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            await db.Users.Where(u => u.Id == order.UserId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp));
            var current = await db.PaymentOrders.SingleOrDefaultAsync(o => o.Id == order.Id && o.UserId == order.UserId);
            if (current == null) return VirtualPaymentRefundReconciliationResult.ReviewRequired;
            if (current.CouponId.HasValue)
                await db.Coupons.Where(c => c.Id == current.CouponId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, c => c.IsEnabled));
            await db.PaymentOrders.Where(o => o.Id == current.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, o => o.Status));
            await db.Entry(current).ReloadAsync();
            var saved = await db.VirtualPaymentSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.PaymentOrderId == current.Id);
            if (saved == null) return VirtualPaymentRefundReconciliationResult.ReviewRequired;
            if (current.Channel != PaymentChannel.WeChatVirtual || current.OutTradeNo != order.OutTradeNo ||
                current.AmountInFen != order.AmountInFen || current.DiscountInFen != order.DiscountInFen ||
                current.CouponId != order.CouponId || current.VipProductId != order.VipProductId ||
                saved.AppId != snapshot.AppId || saved.OpenId != snapshot.OpenId || saved.SignData != snapshot.SignData)
                return VirtualPaymentRefundReconciliationResult.ReviewRequired;
            _ = VirtualPaymentPurchase.Read(current, saved, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            if (await VirtualPaymentEarlyRefundService.IsCompletedAsync(db, current))
                return VirtualPaymentRefundReconciliationResult.Completed;
            var currentReviews = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == current.Id &&
                l.EventType == "VirtualPaymentRefundReview").ToListAsync();
            if (currentReviews.Count != 1 || currentReviews[0].Id != reviews[0].Id ||
                !VirtualPaymentEarlyRefundService.TryReadReview(currentReviews[0], out var currentRefund) || currentRefund != refund)
                return VirtualPaymentRefundReconciliationResult.ReviewRequired;
            if (await settlement.CompleteAsync(current, result) != VirtualPaymentRefundOutcome.Completed)
                return VirtualPaymentRefundReconciliationResult.ReviewRequired;
            db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = current.Id, EventType = "VirtualPaymentRefundReconciled",
                RawData = JsonSerializer.Serialize(new { actorId, reviewId = reviews[0].Id }) });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return VirtualPaymentRefundReconciliationResult.Completed;
        });
    }
}

public enum VirtualPaymentRefundReconciliationResult { Completed, ReviewRequired, Unavailable, NotFound }
