using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.VipProductsViewModels;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class ProductSalesService(WeChatExamDbContext db) : IScopedDependency
{
    public async Task<DailyReceiptsTrend> DailyTrendAsync(List<Guid> productIds, int days, DateTime utcNow)
    {
        if (days is not (7 or 30 or 90)) throw new ArgumentOutOfRangeException(nameof(days));
        // Daily operations use Beijing time (UTC+8). Filter on the saved UTC
        // payment timestamp before grouping; missing dates are never guessed.
        var today = utcNow.AddHours(8).Date;
        var start = today.AddDays(1 - days);
        var startUtc = start.AddHours(-8);
        var paid = db.PaymentOrders.AsNoTracking().Where(o => o.VipProductId.HasValue &&
            productIds.Contains(o.VipProductId.Value) && o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0);
        var dated = await paid.Where(o => o.PaidAt >= startUtc && o.PaidAt <= utcNow)
            .GroupBy(o => o.PaidAt!.Value.AddHours(8).Date)
            .Select(g => new DailySalesPoint
            {
                Date = g.Key, RevenueInFen = g.Sum(o => (long)o.AmountInFen), PaidOrderCount = g.Count()
            }).ToDictionaryAsync(p => p.Date);
        return new DailyReceiptsTrend
        {
            Points = Enumerable.Range(0, days).Select(offset => start.AddDays(offset))
                .Select(date => dated.GetValueOrDefault(date) ?? new DailySalesPoint { Date = date }).ToList(),
            UndatedOrderCount = await paid.CountAsync(o => !o.PaidAt.HasValue)
        };
    }

    public Task<int> PayingBuyerCountAsync(List<Guid> productIds) => db.PaymentOrders.AsNoTracking()
        .Where(o => o.VipProductId.HasValue && productIds.Contains(o.VipProductId.Value) &&
                    o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0)
        .Select(o => o.UserId).Distinct().CountAsync();

    // One order purchases one product. Memberships also contain manual grants and
    // renewals, so they cannot be used to count sales or reconstruct receipts.
    public async Task<Dictionary<Guid, ProductSalesSummary>> SummariesAsync(List<Guid> productIds)
    {
        return await db.PaymentOrders.AsNoTracking()
            .Where(o => o.VipProductId.HasValue && productIds.Contains(o.VipProductId.Value))
            .GroupBy(o => o.VipProductId!.Value)
            .Select(g => new
            {
                Id = g.Key,
                PaidOrderCount = g.Count(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0),
                BuyerCount = g.Where(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0)
                    .Select(o => o.UserId).Distinct().Count(),
                RevenueInFen = g.Sum(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0 ? (long)o.AmountInFen : 0),
                FreeOrderCount = g.Count(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen == 0),
                RefundedOrderCount = g.Count(o => o.Status == PaymentOrderStatus.Refunded),
                RefundReviewCount = g.Count(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0 &&
                    o.PaymentLogs.Any(l => l.EventType == "VirtualPaymentRefundReview")),
                RefundedAmountInFen = g.Sum(o => o.Status == PaymentOrderStatus.Refunded ? (long)o.AmountInFen : 0)
            }).ToDictionaryAsync(x => x.Id, x => new ProductSalesSummary
            {
                PaidOrderCount = x.PaidOrderCount, BuyerCount = x.BuyerCount, RevenueInFen = x.RevenueInFen,
                FreeOrderCount = x.FreeOrderCount, RefundedOrderCount = x.RefundedOrderCount,
                RefundedAmountInFen = x.RefundedAmountInFen, RefundReviewCount = x.RefundReviewCount
            });
    }

    public async Task<SalesViewModel> DetailsAsync(VipProduct product, string kind, int page, int days = 30)
    {
        var all = db.PaymentOrders.AsNoTracking().Where(o => o.VipProductId == product.Id);
        var paid = all.Where(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen > 0);
        var prices = await paid.GroupBy(o => o.AmountInFen).OrderBy(g => g.Key)
            .Select(g => new SalesPriceGroup
            {
                AmountInFen = g.Key, OrderCount = g.Count(),
                BuyerCount = g.Select(o => o.UserId).Distinct().Count(),
                RevenueInFen = g.Sum(o => (long)o.AmountInFen)
            }).ToListAsync();
        var query = kind switch
        {
            "free" => all.Where(o => o.Status == PaymentOrderStatus.Paid && o.AmountInFen == 0),
            "refunded" => all.Where(o => o.Status == PaymentOrderStatus.Refunded),
            _ => paid
        };
        const int pageSize = 25;
        var count = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)));
        var orders = await query.Include(o => o.VirtualPaymentSnapshot)
            .OrderByDescending(o => o.PaidAt).ThenByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        // Keep orphaned historical receipts, as the existing order list does.
        var userIds = orders.Select(o => o.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        foreach (var order in orders) order.User = users.GetValueOrDefault(order.UserId);
        var summaries = await SummariesAsync([product.Id]);
        return new SalesViewModel
        {
            Product = product, Summary = summaries.GetValueOrDefault(product.Id) ?? new ProductSalesSummary(),
            Prices = prices, Orders = orders, Kind = kind, TotalCount = count, CurrentPage = page, PageSize = pageSize,
            Trend = await DailyTrendAsync([product.Id], days, DateTime.UtcNow)
        };
    }
}
