using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class PaymentFulfillmentTests
{
    private SqliteContext _db = null!;
    private PaymentFulfillmentService _service = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _service = new PaymentFulfillmentService(_db, new CouponService(_db),
            new ChangeService(_db), NullLogger<PaymentFulfillmentService>.Instance);
    }

    [TestCleanup]
    public async Task Cleanup() => await _db.DisposeAsync();

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi)]
    [DataRow(PaymentChannel.WeChatVirtual)]
    public async Task ConfirmedOrderGrantsMembershipAndConsumesCouponOnlyOnce(PaymentChannel channel)
    {
        var order = await CreateOrderAsync(channel);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        // These are trusted synthetic results, not calls to either payment gateway.
        await _service.CompleteAsync(order, "confirmed-transaction");
        var end = (await _db.VipMemberships.SingleAsync()).EndTime;
        var paidAt = order.PaidAt;
        await _service.CompleteAsync(order, "duplicate-must-not-overwrite");
        await transaction.CommitAsync();
        _db.ChangeTracker.Clear();

        var saved = await _db.PaymentOrders.SingleAsync();
        Assert.AreEqual(channel, saved.Channel);
        Assert.AreEqual(PaymentOrderStatus.Paid, saved.Status);
        Assert.AreEqual("confirmed-transaction", saved.WechatTransactionId);
        Assert.AreEqual(paidAt, saved.PaidAt);
        Assert.AreEqual(6800, saved.AmountInFen);
        Assert.AreEqual(13000, saved.DiscountInFen);
        var membership = await _db.VipMemberships.SingleAsync();
        Assert.AreEqual(order.Id, membership.LastPaymentOrderId);
        Assert.AreEqual(end, membership.EndTime);
        Assert.AreEqual(30, (membership.EndTime - membership.StartTime).TotalDays);
        var usage = await _db.CouponUsages.SingleAsync();
        Assert.AreEqual(order.Id, usage.PaymentOrderId);
        Assert.AreEqual(13000, usage.DiscountInFen);
        Assert.IsTrue((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(order.UserId, (await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.AreEqual(ChangeType.VipActivatedViaPayment, (await _db.Changes.SingleAsync()).Type);
        Assert.AreEqual("StatusChanged", (await _db.PaymentLogs.SingleAsync()).EventType);
    }

    [TestMethod]
    public async Task RelationalFulfillmentWithoutTransactionCannotPartiallyGrantAnything()
    {
        var order = await CreateOrderAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.CompleteAsync(order, "confirmed"));
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.IsNull(order.PaidAt);
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.IsFalse((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
    }

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi)]
    [DataRow(PaymentChannel.WeChatVirtual)]
    public async Task AccountingFailureRollsBackAllEffectsAndAllowsTheSameOrderToRetry(PaymentChannel channel)
    {
        var order = await CreateOrderAsync(channel);
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_fulfillment_accounting BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'Synthetic accounting failure'); END;");
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => _service.CompleteAsync(order, "confirmed"));
            await transaction.RollbackAsync();
        }
        _db.ChangeTracker.Clear();
        order = await _db.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.IsNull(order.PaidAt);
        Assert.IsNull(order.WechatTransactionId);
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync());
        Assert.IsFalse((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_fulfillment_accounting;");
        await using var retry = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, "confirmed");
        await retry.CommitAsync();
        Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    public async Task RefundedOrderCannotBeReactivatedByALatePaymentResult()
    {
        var order = await CreateOrderAsync();
        order.Status = PaymentOrderStatus.Refunded;
        await _db.SaveChangesAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, "late-transaction");
        await transaction.CommitAsync();
        Assert.AreEqual(PaymentOrderStatus.Refunded, order.Status);
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
    }

    private async Task<PaymentOrder> CreateOrderAsync(PaymentChannel channel = PaymentChannel.WeChatJsApi)
    {
        var user = new User { Id = Guid.NewGuid().ToString(), UserName = "buyer", DisplayName = "Buyer" };
        var product = new VipProduct { Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        var coupon = new Coupon { Code = "CONFIRMED130", AmountInFen = 13000, IsSingleUse = true,
            DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } };
        var order = new PaymentOrder { OutTradeNo = "CONFIRMED-ORDER", User = user, UserId = user.Id,
            VipProduct = product, Coupon = coupon, DiscountInFen = 13000, AmountInFen = 6800,
            Description = "Reserved purchase", Channel = channel };
        _db.AddRange(order, new UserClaimedCoupon { UserId = user.Id, CouponId = coupon.Id });
        await _db.SaveChangesAsync();
        return order;
    }
}
