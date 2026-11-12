using System.Text.Json;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class VirtualPaymentSnapshotMigrationTests
{
    [TestMethod]
    public async Task AdditiveSnapshotTablePreservesFinancialHistoryAndConstrainsNewRows()
    {
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddVirtualPaymentSnapshots", StringComparison.Ordinal));
        Assert.IsTrue(index > 0);
        var previous = migrations[index - 1];
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);

        var user = new User { Id = "snapshot-buyer", UserName = "snapshot-buyer", DisplayName = "Historical buyer" };
        var product = new VipProduct { Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        var coupon = new Coupon { Code = "HISTORY130", AmountInFen = 13000, DistributionChannel = new DistributionChannel { AgencyName = "Agency" } };
        db.AddRange(user, product.TotalCategory!, coupon);
        await db.SaveChangesAsync();
        // Seed with the historical writer's columns; the new sync revision did not exist yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VipProducts (Id,Name,Type,TotalCategoryId,InstitutionId,PriceInFen,DurationDays,IsEnabled,CreationTime) VALUES ({product.Id},{product.Name},{(int)product.Type},{product.TotalCategory?.Id},{product.InstitutionId},{product.PriceInFen},{product.DurationDays},{product.IsEnabled},{product.CreationTime})");

        var paidId = Guid.NewGuid();
        foreach (var status in new[] { PaymentOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Closed })
            db.PaymentOrders.Add(new PaymentOrder { Id = status == PaymentOrderStatus.Paid ? paidId : Guid.NewGuid(),
                OutTradeNo = "HISTORICAL-" + status, UserId = user.Id, VipProductId = product.Id, CouponId = coupon.Id,
                DiscountInFen = 13000, AmountInFen = 6800, Status = status, Description = "Original order" });
        db.AddRange(new PaymentLog { PaymentOrderId = paidId, EventType = "Notified", RawData = "Original notification" },
            new VipMembership { UserId = user.Id, VipProductId = product.Id, LastPaymentOrderId = paidId,
                StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddDays(30) },
            new UserClaimedCoupon { UserId = user.Id, CouponId = coupon.Id, IsUsed = true },
            new CouponUsage { UserId = user.Id, CouponId = coupon.Id, PaymentOrderId = paidId, DiscountInFen = 13000 },
            new Change { TargetUserId = user.Id, TriggerUserId = user.Id, VipProductId = product.Id, CouponId = coupon.Id,
                Type = ChangeType.VipActivatedViaPayment, Details = "Original accounting" });
        await db.SaveChangesAsync();
        var before = await FinancialHistoryAsync(db);
        db.ChangeTracker.Clear();

        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        Assert.AreEqual(0, await db.VirtualPaymentSnapshots.CountAsync());
        // Roll back only the empty new table, before virtual purchases are enabled.
        // Rolling back an app binary does not require dropping populated snapshots.
        await migrator.MigrateAsync(previous);
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db));

        // Previous binaries can keep creating ordinary orders without a snapshot.
        var oldWriterId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PaymentOrders (Id,OutTradeNo,UserId,Description,AmountInFen,DiscountInFen,Status,CreatedAt,ExpiredAt) VALUES ({oldWriterId},{"OLD-WRITER"},{user.Id},{"Original client"},19800,0,0,{DateTime.UtcNow},{DateTime.UtcNow.AddMinutes(30)})");
        Assert.AreEqual(PaymentChannel.WeChatJsApi, (await db.PaymentOrders.SingleAsync(o => o.Id == oldWriterId)).Channel);
        Assert.AreEqual(0, await db.VirtualPaymentSnapshots.CountAsync());

        // Persistence constraints are independent of the later signing API.
        // These synthetic rows are never offered to a client or payment gateway.
        var virtualOrder = new PaymentOrder { OutTradeNo = "VIRTUAL-ORDER", UserId = user.Id, Description = "New virtual purchase",
            Channel = PaymentChannel.WeChatVirtual, AmountInFen = 19800 };
        db.PaymentOrders.Add(virtualOrder);
        await db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO VirtualPaymentSnapshots (PaymentOrderId,AppId,OpenId,SignData) VALUES ({Guid.NewGuid()},{"app"},{"openid"},{"{}"})"));
        db.VirtualPaymentSnapshots.Add(new VirtualPaymentSnapshot { PaymentOrderId = virtualOrder.Id,
            AppId = "app", OpenId = "openid", SignData = "{\"env\":1}" });
        await db.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO VirtualPaymentSnapshots (PaymentOrderId,AppId,OpenId,SignData) VALUES ({virtualOrder.Id},{"app"},{"openid"},{"{}"})"));
        await Assert.ThrowsExactlyAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM PaymentOrders WHERE Id={virtualOrder.Id}"));
        Assert.AreEqual(1, await db.VirtualPaymentSnapshots.CountAsync());
        Assert.IsTrue(await db.PaymentOrders.AnyAsync(o => o.Id == virtualOrder.Id));
        Assert.AreEqual(1, await db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task PlatformReferenceMigrationPreservesOldSnapshotsFinancialHistoryAndOldWriters()
    {
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddVirtualPaymentPlatformReference", StringComparison.Ordinal));
        Assert.IsTrue(index > 0);
        var previous = migrations[index - 1];
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        var user = new User { Id = "reference-buyer", UserName = "reference-buyer", DisplayName = "Historical buyer" };
        var product = new VipProduct { Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        db.AddRange(user, product.TotalCategory!); await db.SaveChangesAsync();
        // Seed with the historical writer's columns; the new sync revision did not exist yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VipProducts (Id,Name,Type,TotalCategoryId,InstitutionId,PriceInFen,DurationDays,IsEnabled,CreationTime) VALUES ({product.Id},{product.Name},{(int)product.Type},{product.TotalCategory?.Id},{product.InstitutionId},{product.PriceInFen},{product.DurationDays},{product.IsEnabled},{product.CreationTime})");
        db.Attach(product);
        var paid = new PaymentOrder { OutTradeNo = "OLD-VIRTUAL-PAID", UserId = user.Id, VipProduct = product,
            Description = "Old paid order", AmountInFen = 19800, Status = PaymentOrderStatus.Paid,
            Channel = PaymentChannel.WeChatVirtual, PaidAt = DateTime.UtcNow };
        var pending = new PaymentOrder { OutTradeNo = "OLD-VIRTUAL-PENDING", UserId = user.Id,
            Description = "Old pending order", Channel = PaymentChannel.WeChatVirtual };
        db.AddRange(paid, pending,
            new PaymentLog { PaymentOrderId = paid.Id, EventType = "VirtualPaymentConfirmed", RawData = "Old proof retained exactly" },
            new VipMembership { UserId = user.Id, VipProduct = product, LastPaymentOrderId = paid.Id,
                StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddDays(30) });
        await db.SaveChangesAsync();
        const string signData = "{ \"productId\": \"课程\", \"env\": 1 }";
        foreach (var id in new[] { paid.Id, pending.Id })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VirtualPaymentSnapshots (PaymentOrderId,AppId,OpenId,SignData) VALUES ({id},{"app"},{"buyer"},{signData})");
        var before = await FinancialHistoryAsync(db);
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        Assert.AreEqual(2, await db.VirtualPaymentSnapshots.CountAsync(s => s.PlatformOrderId == null && s.SignData == signData));
        // A binary rollback keeps the column. Schema downgrade is tested only
        // while every new reference is null, so it discards no payment evidence.
        await migrator.MigrateAsync(previous);
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        await migrator.MigrateAsync();
        Assert.AreEqual(before, await FinancialHistoryAsync(db));
        Assert.AreEqual(2, await db.VirtualPaymentSnapshots.CountAsync(s => s.PlatformOrderId == null));
        var oldWriter = new PaymentOrder { OutTradeNo = "OLD-WRITER-REFERENCE", UserId = user.Id,
            Description = "Old writer", Channel = PaymentChannel.WeChatVirtual };
        db.PaymentOrders.Add(oldWriter); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VirtualPaymentSnapshots (PaymentOrderId,AppId,OpenId,SignData) VALUES ({oldWriter.Id},{"app"},{"buyer"},{signData})");
        Assert.IsNull((await db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync(s => s.PaymentOrderId == oldWriter.Id)).PlatformOrderId);
        await db.VirtualPaymentSnapshots.Where(s => s.PaymentOrderId == paid.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.PlatformOrderId, "platform-paid"));
        Assert.AreEqual(paid.Id, await db.VirtualPaymentSnapshots.Where(s => s.AppId == "app" && s.PlatformOrderId == "platform-paid")
            .Select(s => s.PaymentOrderId).SingleAsync());
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT PaymentOrderId FROM VirtualPaymentSnapshots WHERE AppId='app' AND PlatformOrderId='platform-paid'";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.Contains("IX_VirtualPaymentSnapshots_AppId_PlatformOrderId", reader.GetString(3));
    }

    private static async Task<string> FinancialHistoryAsync(SqliteContext db)
    {
        var history = new List<string>();
        foreach (var table in new[] { "PaymentOrders", "PaymentLogs", "VipMemberships", "Coupons", "UserClaimedCoupons", "CouponUsages", "Changes" })
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fields = new Dictionary<string, string?>();
                for (var i = 0; i < reader.FieldCount; i++) fields.Add(reader.GetName(i), reader.GetValue(i).ToString());
                history.Add(table + JsonSerializer.Serialize(fields));
            }
        }
        return string.Join('\n', history);
    }
}
