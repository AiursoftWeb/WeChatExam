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
        db.VipMemberships.Add(new VipMembership { User = second, UserId = second.Id, VipProduct = product, StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddYears(1) });
        await db.SaveChangesAsync();
        var service = new ProductSalesService(db);
        var summaries = await service.SummariesAsync([product.Id]);
        Assert.AreEqual(1, summaries.Count);
        var summary = summaries[product.Id];
        Assert.AreEqual(3, summary.PaidOrderCount);
        Assert.AreEqual(2, summary.BuyerCount);
        Assert.AreEqual(20200L, summary.RevenueInFen);
        Assert.AreEqual(1, summary.FreeOrderCount);
        Assert.AreEqual(1, summary.RefundedOrderCount);
        Assert.AreEqual(500L, summary.RefundedAmountInFen);
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
        Assert.AreEqual(0, await db.PaymentLogs.CountAsync());
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
        Assert.AreEqual(1, first.Summary.BuyerCount);
        Assert.AreEqual(25, first.Orders.Count);
        Assert.AreEqual(2, last.Orders.Count);
        Assert.AreEqual(2, last.CurrentPage);
        Assert.AreEqual(27, first.Orders.Concat(last.Orders).Select(o => o.Id).Distinct().Count());
    }
}
