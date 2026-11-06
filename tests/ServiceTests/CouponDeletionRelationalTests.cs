using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class CouponDeletionRelationalTests
{
    [TestMethod]
    [DataRow("order", false)]
    [DataRow("audit", false)]
    [DataRow("claim", false)]
    [DataRow("usage", false)]
    [DataRow("order", true)]
    [DataRow("audit", true)]
    [DataRow("claim", true)]
    [DataRow("usage", true)]
    public async Task RelationalDeletionKeepsHistoryInsteadOfCascadingItOrThrowingAConstraintError(string history, bool deleteChannel)
    {
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        var user = new User { UserName = "buyer", DisplayName = "Buyer" };
        var channel = new DistributionChannel { AgencyName = "Agency" };
        var coupon = new Coupon { Code = "HISTORY", DistributionChannel = channel };
        db.AddRange(user, coupon);
        var order = new PaymentOrder { User = user, UserId = user.Id, OutTradeNo = "Order", Description = "Unpaid", Status = PaymentOrderStatus.Pending };
        switch (history)
        {
            case "order": order.Coupon = coupon; db.Add(order); break;
            case "audit": db.Changes.Add(new Change { TargetUserId = user.Id, Coupon = coupon }); break;
            case "claim": db.UserClaimedCoupons.Add(new UserClaimedCoupon { UserId = user.Id, Coupon = coupon }); break;
            // A usage record without a direct order-to-coupon link must also survive.
            case "usage": db.CouponUsages.Add(new CouponUsage { UserId = user.Id, Coupon = coupon, PaymentOrder = order }); break;
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new CouponService(db);
        // Both paths must preserve the same persisted history.
        var task = deleteChannel
            ? new DistributionChannelService(db).DeleteAsync(channel.Id)
            : service.DeleteAsync(coupon.Id);
        var rejected = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => task);
        Assert.AreEqual(CouponDeletionSummary.HistoryExists, rejected.Message);
        db.ChangeTracker.Clear();
        Assert.IsTrue(await db.Coupons.AnyAsync(c => c.Id == coupon.Id));
        Assert.IsTrue(await db.DistributionChannels.AnyAsync(c => c.Id == channel.Id));
        Assert.IsFalse((await service.GetDeletionSummaryAsync(coupon.Id)).CanDelete);
    }
}
