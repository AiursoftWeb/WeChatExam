using System.Text.Json;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaymentChannelMigrationTests
{
    [TestMethod]
    public async Task HistoricalPaymentsAndTheirFinancialReferencesSurviveUpgradeAndRollback()
    {
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddPaymentChannel", StringComparison.Ordinal));
        Assert.IsTrue(index > 0);
        var previous = migrations[index - 1];
        await migrator.MigrateAsync(previous);

        var user = new User { UserName = "channel-migration", DisplayName = "Historical buyer" };
        var product = new VipProduct { Name = "Historical course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        var coupon = new Coupon { Code = "HISTORICAL130", AmountInFen = 13000, DistributionChannel = new DistributionChannel { AgencyName = "Historical agency" } };
        db.AddRange(user, product.TotalCategory!, coupon);
        await db.SaveChangesAsync();
        // Seed with the historical writer's columns; the new sync revision did not exist yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VipProducts (Id,Name,Type,TotalCategoryId,InstitutionId,PriceInFen,DurationDays,IsEnabled,CreationTime) VALUES ({product.Id},{product.Name},{(int)product.Type},{product.TotalCategory?.Id},{product.InstitutionId},{product.PriceInFen},{product.DurationDays},{product.IsEnabled},{product.CreationTime})");


        var now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var paidId = Guid.NewGuid();
        foreach (var status in new[] { PaymentOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Closed })
        {
            var id = status == PaymentOrderStatus.Paid ? paidId : Guid.NewGuid();
            var number = $"LEGACY-{status}";
            var transactionId = status == PaymentOrderStatus.Paid ? "legacy-transaction" : null;
            DateTime? paidAt = status == PaymentOrderStatus.Paid ? now : null;
            // Use the old schema explicitly: no channel column exists at this point.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO PaymentOrders
                    (Id, OutTradeNo, UserId, VipProductId, CouponId, DiscountInFen, Description,
                     AmountInFen, Status, WechatTransactionId, PrepayId, CreatedAt, PaidAt, ExpiredAt)
                VALUES ({id}, {number}, {user.Id}, {product.Id}, {coupon.Id}, 13000, {"Original purchase"},
                        6800, {(int)status}, {transactionId}, {"legacy-prepay"}, {now}, {paidAt}, {now.AddMinutes(30)})
                """);
        }
        db.AddRange(
            new PaymentLog { PaymentOrderId = paidId, EventType = "Notified", RawData = "Historical gateway payload" },
            new VipMembership { UserId = user.Id, VipProductId = product.Id, LastPaymentOrderId = paidId, StartTime = now, EndTime = now.AddDays(30) },
            new UserClaimedCoupon { UserId = user.Id, CouponId = coupon.Id, IsUsed = true },
            new CouponUsage { UserId = user.Id, CouponId = coupon.Id, PaymentOrderId = paidId, DiscountInFen = 13000 });
        await db.SaveChangesAsync();
        var before = await FinancialHistoryAsync(db);
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db), "Adding a channel must not reprice orders or alter existing entitlements, claims or accounting references.");
        var orders = await db.PaymentOrders.AsNoTracking().ToListAsync();
        Assert.HasCount(3, orders);
        Assert.IsTrue(orders.All(o => o.Channel == PaymentChannel.WeChatJsApi));

        // This additive migration can be rolled back without losing the old data.
        await migrator.MigrateAsync(previous);
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db));

        // A still-running old binary also omits the new column after upgrade.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PaymentOrders (Id, OutTradeNo, UserId, DiscountInFen, Description, AmountInFen, Status, CreatedAt, ExpiredAt) VALUES ({Guid.NewGuid()}, {"OLD-WRITER"}, {user.Id}, 0, {"Old client order"}, 19800, 0, {now}, {now.AddMinutes(30)})");
        var oldWriter = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.OutTradeNo == "OLD-WRITER");
        Assert.AreEqual(PaymentChannel.WeChatJsApi, oldWriter.Channel);
        Assert.AreEqual(19800, oldWriter.AmountInFen);

        db.PaymentOrders.Add(new PaymentOrder { OutTradeNo = "NEW-WRITER", UserId = user.Id, Description = "Existing checkout", AmountInFen = 19800 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.AreEqual(PaymentChannel.WeChatJsApi, (await db.PaymentOrders.SingleAsync(o => o.OutTradeNo == "NEW-WRITER")).Channel);
    }

    private static async Task<string> FinancialHistoryAsync(SqliteContext db)
    {
        var history = new List<string>();
        foreach (var table in new[] { "PaymentOrders", "PaymentLogs", "VipMemberships", "Coupons", "UserClaimedCoupons", "CouponUsages" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // Compare every historical field; only the newly added discriminator is excluded.
                var values = new Dictionary<string, string?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    if (table == "PaymentOrders" && name == "Channel") continue;
                    values.Add(name, reader.GetValue(i).ToString());
                }
                history.Add(table + JsonSerializer.Serialize(values));
            }
        }
        return string.Join('\n', history);
    }
}
