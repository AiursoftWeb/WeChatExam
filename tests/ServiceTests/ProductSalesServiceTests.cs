using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class ProductSalesServiceTests
{
    [TestMethod]
    public async Task SqliteAggregatesSavedReceiptsDeduplicatesBuyersAndSeparatesFreeRefundedAndUnpaid()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var product = new VipProduct { Name = "Changed price", PriceInFen = 49900, TotalCategory = new TotalCategory { Title = "Course" }, IsEnabled = false };
        var other = new VipProduct { Name = "Other", TotalCategory = new TotalCategory { Title = "Other" } };
        var buyer = new User { DisplayName = "Returning buyer" };
        var second = new User { DisplayName = "Second buyer" };
        db.AddRange(product, other, buyer, second);
        PaymentOrder Order(VipProduct? item, User user, PaymentOrderStatus status, int amount, int discount = 0) => new()
        {
            VipProduct = item, UserId = user.Id, User = user, Status = status, AmountInFen = amount, DiscountInFen = discount,
            OutTradeNo = Guid.NewGuid().ToString("N"), Description = "Historical sale", PaidAt = DateTime.UtcNow
        };
        db.PaymentOrders.AddRange(Order(product, buyer, PaymentOrderStatus.Paid, 200, 19600),
            Order(product, buyer, PaymentOrderStatus.Paid, 19800), Order(product, second, PaymentOrderStatus.Paid, 200),
            Order(product, buyer, PaymentOrderStatus.Paid, 0), Order(product, second, PaymentOrderStatus.Refunded, 500),
            Order(product, second, PaymentOrderStatus.Pending, 99900), Order(product, second, PaymentOrderStatus.Failed, 99900),
            Order(product, second, PaymentOrderStatus.Closed, 99900), Order(other, buyer, PaymentOrderStatus.Paid, 99900),
            Order(null, buyer, PaymentOrderStatus.Paid, 99900));
        // Partial or uncertain refunds retain Paid until manual reconciliation.
        // Repeated review logs must flag an order only once.
        var reviewed = db.PaymentOrders.Local.First();
        for (var i = 0; i < 2; i++)
            db.PaymentLogs.Add(new PaymentLog { PaymentOrder = reviewed, PaymentOrderId = reviewed.Id,
                EventType = "VirtualPaymentRefundReview", RawData = "{}" });
        db.VipMemberships.Add(new VipMembership { User = second, UserId = second.Id, VipProduct = product, StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddYears(1) });
        await db.SaveChangesAsync();
        var service = new ProductSalesService(db);
        Assert.AreEqual(2, await service.PayingBuyerCountAsync([product.Id, other.Id]));
        Assert.AreEqual(1, await service.PayingBuyerCountAsync([other.Id]));
        Assert.AreEqual(0, await service.PayingBuyerCountAsync([]));
        var summaries = await service.SummariesAsync([product.Id]);
        Assert.AreEqual(1, summaries.Count);
        var summary = summaries[product.Id];
        Assert.AreEqual(3, summary.PaidOrderCount);
        Assert.AreEqual(2, summary.BuyerCount);
        Assert.AreEqual(20200L, summary.RevenueInFen);
        Assert.AreEqual(1, summary.FreeOrderCount);
        Assert.AreEqual(1, summary.RefundedOrderCount);
        Assert.AreEqual(500L, summary.RefundedAmountInFen);
        Assert.AreEqual(1, summary.RefundReviewCount);
        var details = await service.DetailsAsync(product, "paid", 0);
        Assert.AreEqual(1, details.CurrentPage);
        Assert.AreEqual(3, details.TotalCount);
        Assert.AreEqual(2, details.Prices.Count);
        Assert.AreEqual(200, details.Prices[0].AmountInFen);
        Assert.AreEqual(2, details.Prices[0].OrderCount);
        Assert.AreEqual(2, details.Prices[0].BuyerCount);
        Assert.AreEqual(400L, details.Prices[0].RevenueInFen);
        Assert.AreEqual(summary.RevenueInFen, details.Prices.Sum(p => p.RevenueInFen));
        Assert.AreEqual(1, (await service.DetailsAsync(product, "free", int.MaxValue)).TotalCount);
        Assert.AreEqual(500, (await service.DetailsAsync(product, "refunded", 1)).Orders.Single().AmountInFen);
        Assert.AreEqual(10, await db.PaymentOrders.CountAsync());
        Assert.AreEqual(1, await db.VipMemberships.CountAsync());
        Assert.AreEqual(2, await db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    public async Task DailyTrendUsesBeijingPaymentDatesFillsGapsAndKeepsFilteredSavedReceipts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var now = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var startUtc = new DateTime(2026, 11, 7, 16, 0, 0, DateTimeKind.Utc);
        var midnight = new DateTime(2026, 11, 13, 16, 0, 0, DateTimeKind.Utc);
        var product = new VipProduct { Name = "Trend", PriceInFen = 49900,
            TotalCategory = new TotalCategory { Title = "Course" } };
        var other = new VipProduct { Name = "Other", IsEnabled = false,
            TotalCategory = new TotalCategory { Title = "Other" } };
        var buyer = new User { DisplayName = "Buyer" };
        db.AddRange(product, other, buyer);
        PaymentOrder Order(DateTime? at, int amount, PaymentOrderStatus status = PaymentOrderStatus.Paid,
            VipProduct? target = null) => new()
        {
            VipProduct = target ?? product, User = buyer, UserId = buyer.Id, Status = status, AmountInFen = amount,
            PaidAt = at, CreatedAt = now, OutTradeNo = Guid.NewGuid().ToString("N"), Description = "Saved daily receipt"
        };
        db.PaymentOrders.AddRange(Order(startUtc.AddTicks(-1), 999), Order(startUtc, 200),
            Order(midnight.AddSeconds(-1), 300), Order(midnight, 400), Order(now, 500),
            Order(now.AddSeconds(1), 999), Order(now.AddDays(1), 999), Order(now, 0),
            Order(now, 700, PaymentOrderStatus.Refunded), Order(now, 999, PaymentOrderStatus.Pending),
            Order(null, 600), Order(now, 900, target: other));
        await db.SaveChangesAsync();
        var service = new ProductSalesService(db);
        var trend = await service.DailyTrendAsync([product.Id], 7, now);
        Assert.AreEqual(7, trend.Points.Count);
        CollectionAssert.AreEqual(Enumerable.Range(0, 7).Select(i => new DateTime(2026, 11, 8).AddDays(i)).ToArray(),
            trend.Points.Select(p => p.Date).ToArray());
        Assert.AreEqual(1400L, trend.RevenueInFen);
        Assert.AreEqual(4, trend.PaidOrderCount);
        Assert.AreEqual(200L, trend.Points[0].RevenueInFen);
        Assert.AreEqual(300L, trend.Points[^2].RevenueInFen);
        Assert.AreEqual(900L, trend.Points[^1].RevenueInFen);
        Assert.AreEqual(2, trend.Points[^1].PaidOrderCount);
        Assert.AreEqual(4, trend.Points.Count(p => p.RevenueInFen == 0 && p.PaidOrderCount == 0));
        Assert.AreEqual(1, trend.UndatedOrderCount);
        Assert.AreEqual(2300L, (await service.DailyTrendAsync([product.Id, other.Id], 7, now)).RevenueInFen);
        var empty = await service.DailyTrendAsync([], 90, now);
        Assert.AreEqual(90, empty.Points.Count);
        Assert.AreEqual(0L, empty.RevenueInFen);
        Assert.AreEqual(0, empty.UndatedOrderCount);
        Assert.AreEqual(12, await db.PaymentOrders.CountAsync());
    }

    [TestMethod]
    public async Task ReceiptsUseLongSumsAndPaginationKeepsEveryPurchase()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var product = new VipProduct { Name = "Large receipts", TotalCategory = new TotalCategory { Title = "Course" } };
        var buyer = new User { DisplayName = "Buyer" };
        db.AddRange(product, buyer);
        for (var i = 0; i < 27; i++)
            db.PaymentOrders.Add(new PaymentOrder { VipProduct = product, UserId = buyer.Id, User = buyer,
                OutTradeNo = $"LARGE-{i}", Description = "Sale", Status = PaymentOrderStatus.Paid, AmountInFen = 100000000,
                PaidAt = DateTime.UtcNow.AddMinutes(-i) });
        await db.SaveChangesAsync();
        var service = new ProductSalesService(db);
        var first = await service.DetailsAsync(product, "paid", 1);
        var last = await service.DetailsAsync(product, "paid", int.MaxValue);
        Assert.AreEqual(2700000000L, first.Summary.RevenueInFen);
        Assert.AreEqual(2700000000L, first.Trend!.RevenueInFen);
        Assert.AreEqual(1, first.Summary.BuyerCount);
        Assert.AreEqual(25, first.Orders.Count);
        Assert.AreEqual(2, last.Orders.Count);
        Assert.AreEqual(2, last.CurrentPage);
        Assert.AreEqual(27, first.Orders.Concat(last.Orders).Select(o => o.Id).Distinct().Count());
    }
}
