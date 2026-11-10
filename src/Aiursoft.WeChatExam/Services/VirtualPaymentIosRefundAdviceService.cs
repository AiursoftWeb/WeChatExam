using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Read-only facts for an AUTHENTICATED platform refund enquiry, not a public
/// endpoint or refund authorizer. The future transport must resolve the original
/// payment and verify the platform message before calling this internal boundary.
/// A missing answer means insufficient evidence, never automatic refund approval.
/// </summary>
public class VirtualPaymentIosRefundAdviceService(WeChatExamDbContext db, IOptions<AppSettings> options) : IScopedDependency
{
    public async Task<VirtualPaymentIosRefundAdvice?> GetAdviceAsync(VirtualPaymentIosRefundEnquiry enquiry,
        CancellationToken cancellationToken = default)
    {
        // Never attest to our own uncommitted membership grant or a client claim.
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null ||
            System.Transactions.Transaction.Current != null || enquiry.PaymentOrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(enquiry.PlatformOrderId) || enquiry.PlatformOrderId.Length > 128 ||
            string.IsNullOrWhiteSpace(enquiry.ProductId) || enquiry.ProductId.Length > 128 ||
            enquiry.Quantity != 1 || enquiry.ProvidedStatus != 1) return null;
        var now = DateTime.UtcNow;
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == enquiry.PaymentOrderId &&
            o.Channel == PaymentChannel.WeChatVirtual && o.Status == PaymentOrderStatus.Paid, cancellationToken);
        if (order?.PaidAt == null || order.PaidAt > now || !order.VipProductId.HasValue || order.AmountInFen <= 0) return null;
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
        if (snapshot == null) return null;
        VirtualPaymentPurchase purchase;
        try { purchase = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId); }
        catch (InvalidOperationException) { return null; }
        if (purchase.ProductId != enquiry.ProductId || purchase.BuyQuantity != enquiry.Quantity) return null;

        var logs = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            (l.EventType == "VirtualPaymentConfirmed" || l.EventType == "StatusChanged"))
            .Select(l => new { l.EventType, l.RawData }).ToListAsync(cancellationToken);
        var payments = logs.Where(l => l.EventType == "VirtualPaymentConfirmed").ToList();
        var grants = logs.Where(l => l.EventType == "StatusChanged").ToList();
        if (payments.Count != 1 || grants.Count != 1) return null;
        try
        {
            using var json = JsonDocument.Parse(payments[0].RawData ?? "");
            if (json.RootElement.GetProperty("OutTradeNo").GetString() != order.OutTradeNo ||
                json.RootElement.GetProperty("PlatformOrderId").GetString() != enquiry.PlatformOrderId) return null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
        var receipt = VirtualPaymentEntitlementReceipt.ReadGrant(grants[0].RawData);
        if (receipt == null || receipt.Version != 1 || receipt.MembershipId == Guid.Empty || receipt.UserId != order.UserId ||
            receipt.VipProductId != order.VipProductId || receipt.After.LastPaymentOrderId != order.Id ||
            receipt.After.EndTime <= receipt.After.StartTime || receipt.Before?.LastPaymentOrderId == order.Id ||
            receipt.Before?.EndTime >= receipt.After.EndTime) return null;

        // Check the still-paid original order AND its unchanged active grant in
        // ONE SQL statement. A later purchase, gift, refund or manual edit must
        // not be used as evidence that this purchase is currently delivered.
        var delivered = await db.PaymentOrders.AsNoTracking().AnyAsync(o => o.Id == order.Id && o.UserId == order.UserId &&
            o.Channel == PaymentChannel.WeChatVirtual && o.Status == PaymentOrderStatus.Paid &&
            o.PaidAt == order.PaidAt && o.VipProductId == order.VipProductId && o.AmountInFen == order.AmountInFen &&
            db.VipMemberships.Any(v => v.Id == receipt.MembershipId && v.UserId == o.UserId && v.VipProductId == o.VipProductId &&
                v.StartTime == receipt.After.StartTime && v.EndTime == receipt.After.EndTime && v.LastPaymentOrderId == o.Id &&
                v.StartTime <= now && v.EndTime > now), cancellationToken);
        if (!delivered) return null;

        // Only delivery facts: no claim that the student read, downloaded or
        // completed anything. Omit buyer IDs, login sessions and coupon details.
        var evidence = JsonSerializer.Serialize(new
        {
            kind = "membership_access_granted", merchantOrder = order.OutTradeNo, platformOrder = enquiry.PlatformOrderId,
            product = purchase.ProductId, quantity = purchase.BuyQuantity,
            paidAt = DateTime.SpecifyKind(order.PaidAt.Value, DateTimeKind.Utc),
            accessStartsAt = DateTime.SpecifyKind(receipt.After.StartTime, DateTimeKind.Utc),
            accessEndsAt = DateTime.SpecifyKind(receipt.After.EndTime, DateTimeKind.Utc)
        });
        return new VirtualPaymentIosRefundAdvice(1, "Digital membership access was delivered for this order.", evidence);
    }
}

/// <summary>Internal, authenticated platform enquiry; this is not a wire-format or client request model.</summary>
public sealed record VirtualPaymentIosRefundEnquiry(Guid PaymentOrderId, string PlatformOrderId,
    string ProductId, int Quantity, int ProvidedStatus);

/// <summary>Platform advice only. Apple still decides; an actual refund follows the separate settlement path.</summary>
public sealed record VirtualPaymentIosRefundAdvice(int ResultCode, string ResultInfo, string Evidence);
