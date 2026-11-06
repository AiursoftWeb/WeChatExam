using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>History belongs to the business record, even when it has no paid orders.</summary>
public class CouponDeletionSummary
{
    public const string HistoryExists = "This coupon or channel has history and cannot be deleted.";
    public int CouponCount { get; init; }
    public int OrderCount { get; init; }
    public int AuditCount { get; init; }
    public int ClaimCount { get; init; }
    public int UsageCount { get; init; }
    public bool HasLegacyUsage { get; init; }
    public bool CanDelete => OrderCount == 0 && AuditCount == 0 && ClaimCount == 0 && UsageCount == 0 && !HasLegacyUsage;

    public static async Task<CouponDeletionSummary> LoadAsync(WeChatExamDbContext context, IQueryable<Coupon> coupons)
    {
        var ids = coupons.Select(c => c.Id);
        return new CouponDeletionSummary
        {
            CouponCount = await coupons.CountAsync(),
            // Pending and unsuccessful orders must retain their coupon reference too.
            OrderCount = await context.PaymentOrders.CountAsync(o => o.CouponId.HasValue && ids.Contains(o.CouponId.Value)),
            AuditCount = await context.Changes.CountAsync(c => c.CouponId.HasValue && ids.Contains(c.CouponId.Value)),
            ClaimCount = await context.UserClaimedCoupons.CountAsync(c => ids.Contains(c.CouponId)),
            UsageCount = await context.CouponUsages.CountAsync(c => ids.Contains(c.CouponId)),
            HasLegacyUsage = await coupons.AnyAsync(c => c.UsedByUserId != null || c.UsedAt != null)
        };
    }
}
