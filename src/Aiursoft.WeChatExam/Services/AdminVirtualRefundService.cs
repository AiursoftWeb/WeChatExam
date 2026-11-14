using System.Data;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Staff-only orchestration. External mutations never run inside an EF retry or a database lock.</summary>
public class AdminVirtualRefundService(WeChatExamDbContext db, IVirtualPaymentQueryService purchases,
    IVirtualPaymentRefundGateway gateway, VirtualPaymentAccessTokenService tokens, VirtualPaymentRefundService settlement,
    IOptions<AppSettings> options, ILogger<AdminVirtualRefundService> logger) : IScopedDependency
{
    public async Task<AdminRefundResult> VerifyPlatformAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Guard();
        var order = await Load(id, cancellationToken);
        if (order?.VirtualPaymentSnapshot is not { } snapshot ||
            order.Status is not (PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded)) return AdminRefundResult.NotEligible;
        VirtualPaymentQueryResult observed;
        try { observed = await ReadPurchase(order, snapshot, cancellationToken); }
        catch (Exception ex) when (Unavailable(ex)) { return AdminRefundResult.Unavailable; }
        if (!Matches(order, snapshot, observed) || observed.Platform is not (VirtualPaymentPlatform.WeChat or VirtualPaymentPlatform.Apple))
            return AdminRefundResult.Unavailable;
        return await Transaction(order, snapshot, async current =>
        {
            var saved = await db.VirtualPaymentSnapshots.SingleAsync(s => s.PaymentOrderId == current.Id);
            if (saved.Platform.HasValue && saved.Platform != observed.Platform) return AdminRefundResult.Unavailable;
            if (saved.Platform == null)
            {
                saved.Platform = observed.Platform;
                Audit(current.Id, "VirtualPaymentPlatformVerified", new { actorId, observed.Platform });
            }
            return AdminRefundResult.Verified;
        });
    }

    public async Task<AdminRefundResult> RequestAsync(Guid id, int expectedAmountInFen, string actorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Guard();
        var order = await Load(id, cancellationToken);
        if (order == null) return AdminRefundResult.NotEligible;
        if (order.RefundRequest != null) return AdminRefundResult.Existing;
        if (order.VirtualPaymentSnapshot?.Platform == VirtualPaymentPlatform.Apple) return AdminRefundResult.AppleManaged;
        if (order.Status != PaymentOrderStatus.Paid || order.PaidAt == null || order.PaidAt < DateTime.UtcNow.AddDays(-365) || order.AmountInFen <= 0 ||
            order.AmountInFen != expectedAmountInFen || order.VirtualPaymentSnapshot is not { Platform: VirtualPaymentPlatform.WeChat } snapshot)
            return AdminRefundResult.NotEligible;
        VirtualPaymentQueryResult observed;
        string token;
        try
        {
            observed = await ReadPurchase(order, snapshot, cancellationToken);
            token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
        }
        catch (Exception ex) when (Unavailable(ex)) { return AdminRefundResult.Unavailable; }
        if (!Matches(order, snapshot, observed) || observed.Platform != VirtualPaymentPlatform.WeChat ||
            observed.State != VirtualPaymentOrderState.Delivered || observed.RemainingInFen != order.AmountInFen ||
            observed.PaidInFen != order.AmountInFen) return AdminRefundResult.NotEligible;

        // Unique order PK + existing buyer/coupon/order lock order serialize all nodes.
        // If a commit's acknowledgement is lost, retry sees the intent and DOES NOT dispatch.
        var reserved = await Transaction(order, snapshot, async current =>
        {
            if (await db.VirtualPaymentRefundRequests.AnyAsync(r => r.PaymentOrderId == current.Id)) return AdminRefundResult.Existing;
            if (current.Status != PaymentOrderStatus.Paid || current.AmountInFen != expectedAmountInFen ||
                await db.PaymentLogs.AnyAsync(l => l.PaymentOrderId == current.Id &&
                    (l.EventType == "VirtualPaymentRefundReview" || l.EventType == "VirtualPaymentRefunded")))
                return AdminRefundResult.NotEligible;
            var request = new VirtualPaymentRefundRequest { PaymentOrderId = current.Id,
                RefundOrderId = "R" + Guid.NewGuid().ToString("N")[..31], AmountInFen = current.AmountInFen, ActorId = actorId };
            db.Add(request);
            Audit(current.Id, "VirtualPaymentRefundRequested", new { actorId, request.RefundOrderId, request.AmountInFen });
            return AdminRefundResult.Accepted;
        });
        if (reserved != AdminRefundResult.Accepted) return reserved;
        db.ChangeTracker.Clear();
        var intent = await db.VirtualPaymentRefundRequests.AsNoTracking().SingleAsync(r => r.PaymentOrderId == id);
        VirtualRefundSubmission response;
        try
        {
            // An abandoned browser cannot cancel an already committed dispatch.
            // Timeout means UNKNOWN, never permission to start another refund.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            response = await gateway.StartAsync(order, snapshot, intent, token, deadline.Token);
        }
        catch (Exception ex) when (Unavailable(ex))
        {
            logger.LogWarning("Refund submission is unconfirmed for order {OrderId}; query the saved intent.", id);
            return AdminRefundResult.Unavailable;
        }
        return await Transaction(order, snapshot, async current =>
        {
            var saved = await db.VirtualPaymentRefundRequests.SingleAsync(r => r.PaymentOrderId == current.Id);
            if (saved.State == VirtualPaymentRefundRequestState.Completed) return AdminRefundResult.Completed;
            if (saved.RefundOrderId != intent.RefundOrderId ||
                saved.PlatformRefundId != null && saved.PlatformRefundId != response.PlatformRefundId)
                return AdminRefundResult.Unavailable;
            saved.State = response.Accepted ? VirtualPaymentRefundRequestState.Accepted : VirtualPaymentRefundRequestState.Failed;
            saved.PlatformRefundId = response.PlatformRefundId;
            saved.ErrorCode = response.ErrorCode;
            saved.UpdatedAt = DateTime.UtcNow;
            Audit(current.Id, "VirtualPaymentRefundSubmission", new { actorId, saved.RefundOrderId, saved.State, saved.ErrorCode });
            return response.Accepted ? AdminRefundResult.Accepted : AdminRefundResult.Unavailable;
        });
    }

    public async Task<AdminRefundResult> CheckAsync(Guid id, string actorId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Guard();
        var order = await Load(id, cancellationToken);
        if (order?.RefundRequest is not { } request || order.VirtualPaymentSnapshot is not { } snapshot)
            return AdminRefundResult.NotEligible;
        if (request.State == VirtualPaymentRefundRequestState.Completed) return AdminRefundResult.Completed;
        VirtualRefundObservation observed;
        try
        {
            var token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
            try { observed = await gateway.QueryAsync(order, snapshot, request, token, cancellationToken); }
            catch (VirtualPaymentGatewayException ex) when (ex.IsInvalidAccessToken)
            {
                await tokens.ForgetRejectedAsync(snapshot.AppId, token, cancellationToken);
                token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
                observed = await gateway.QueryAsync(order, snapshot, request, token, cancellationToken);
            }
        }
        catch (Exception ex) when (Unavailable(ex)) { return AdminRefundResult.Unavailable; }
        return await Transaction(order, snapshot, async current =>
        {
            var saved = await db.VirtualPaymentRefundRequests.SingleAsync(r => r.PaymentOrderId == current.Id);
            if (saved.State == VirtualPaymentRefundRequestState.Completed) return AdminRefundResult.Completed;
            if (saved.RefundOrderId != request.RefundOrderId || saved.AmountInFen != current.AmountInFen ||
                saved.PlatformRefundId != null && saved.PlatformRefundId != observed.PlatformRefundId)
                return AdminRefundResult.Unavailable;
            saved.PlatformRefundId = observed.PlatformRefundId;
            saved.UpdatedAt = DateTime.UtcNow;
            if (observed.State == VirtualRefundState.Completed)
            {
                _ = await settlement.CompleteAsync(current, new VirtualPaymentRefundConfirmation(current.Id,
                    snapshot.PlatformOrderId!, observed.PlatformRefundId, saved.AmountInFen));
                saved.State = VirtualPaymentRefundRequestState.Completed;
                saved.CompletedAt = DateTime.UtcNow;
            }
            else saved.State = observed.State == VirtualRefundState.Failed
                ? VirtualPaymentRefundRequestState.Failed : VirtualPaymentRefundRequestState.Accepted;
            Audit(current.Id, "VirtualPaymentRefundChecked", new { actorId, saved.RefundOrderId, saved.State });
            return saved.State == VirtualPaymentRefundRequestState.Completed ? AdminRefundResult.Completed : AdminRefundResult.Existing;
        });
    }

    private Task<PaymentOrder?> Load(Guid id, CancellationToken cancellationToken) => db.PaymentOrders.AsNoTracking()
        .Include(o => o.VirtualPaymentSnapshot).Include(o => o.RefundRequest)
        .SingleOrDefaultAsync(o => o.Id == id && o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);

    private async Task<VirtualPaymentQueryResult> ReadPurchase(PaymentOrder order, VirtualPaymentSnapshot snapshot, CancellationToken cancellationToken)
    {
        _ = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
        var token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
        try { return await purchases.QueryAsync(order, snapshot, token, cancellationToken); }
        catch (VirtualPaymentGatewayException ex) when (ex.IsInvalidAccessToken)
        {
            await tokens.ForgetRejectedAsync(snapshot.AppId, token, cancellationToken);
            token = await tokens.GetAsync(snapshot.AppId, cancellationToken);
            return await purchases.QueryAsync(order, snapshot, token, cancellationToken);
        }
    }

    private async Task<AdminRefundResult> Transaction(PaymentOrder observed, VirtualPaymentSnapshot snapshot,
        Func<PaymentOrder, Task<AdminRefundResult>> action)
    {
        var retrying = false;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) db.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            await db.Users.Where(u => u.Id == observed.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp));
            if (observed.CouponId.HasValue)
                await db.Coupons.Where(c => c.Id == observed.CouponId).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsEnabled, c => c.IsEnabled));
            await db.PaymentOrders.Where(o => o.Id == observed.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, o => o.Status));
            var current = await db.PaymentOrders.SingleAsync(o => o.Id == observed.Id);
            await db.Entry(current).ReloadAsync();
            var saved = await db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync(s => s.PaymentOrderId == current.Id);
            if (current.UserId != observed.UserId || current.Channel != PaymentChannel.WeChatVirtual ||
                current.OutTradeNo != observed.OutTradeNo || current.AmountInFen != observed.AmountInFen ||
                current.DiscountInFen != observed.DiscountInFen || current.CouponId != observed.CouponId ||
                current.VipProductId != observed.VipProductId || saved.SignData != snapshot.SignData ||
                saved.AppId != snapshot.AppId || saved.OpenId != snapshot.OpenId || saved.PlatformOrderId != snapshot.PlatformOrderId ||
                saved.Platform.HasValue && snapshot.Platform.HasValue && saved.Platform != snapshot.Platform)
                return AdminRefundResult.Unavailable;
            _ = VirtualPaymentPurchase.Read(current, saved, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            var result = await action(current);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        });
    }

    private static bool Matches(PaymentOrder order, VirtualPaymentSnapshot snapshot, VirtualPaymentQueryResult result) =>
        result.PaymentOrderId == order.Id && result.AmountInFen == order.AmountInFen &&
        !string.IsNullOrWhiteSpace(snapshot.PlatformOrderId) && result.PlatformOrderId == snapshot.PlatformOrderId &&
        (!snapshot.Platform.HasValue || result.Platform == snapshot.Platform);

    private void Guard()
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null ||
            System.Transactions.Transaction.Current != null || db.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Staff refunds require an independent relational operation.");
    }

    private void Audit(Guid orderId, string kind, object details) => db.PaymentLogs.Add(new PaymentLog
        { PaymentOrderId = orderId, EventType = kind, RawData = JsonSerializer.Serialize(details) });

    private static bool Unavailable(Exception ex) => ex is InvalidOperationException or OperationCanceledException or JsonException or KeyNotFoundException or FormatException or OverflowException;
}

public enum AdminRefundResult { Verified, Accepted, Existing, Completed, Unavailable, NotEligible, AppleManaged }
