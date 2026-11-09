using System.Text.Json;
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
        var log = await _db.PaymentLogs.SingleAsync();
        Assert.AreEqual("StatusChanged", log.EventType);
        if (channel == PaymentChannel.WeChatJsApi)
            Assert.AreEqual("{\"newStatus\":\"Paid\",\"transactionId\":\"confirmed-transaction\"}", log.RawData);
        else
        {
            var receipt = ReadReceipt(log);
            Assert.AreEqual(1, receipt.Version); Assert.AreEqual(membership.Id, receipt.MembershipId);
            Assert.AreEqual(order.UserId, receipt.UserId); Assert.AreEqual(order.VipProductId, receipt.VipProductId);
            Assert.IsNull(receipt.Before);
            Assert.AreEqual(membership.StartTime, receipt.After.StartTime);
            Assert.AreEqual(membership.EndTime, receipt.After.EndTime);
            Assert.AreEqual(order.Id, receipt.After.LastPaymentOrderId);
        }
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
    [DataRow(PaymentChannel.WeChatJsApi)]
    [DataRow(PaymentChannel.WeChatVirtual)]
    public async Task RefundedOrderCannotBeReactivatedByALatePaymentResult(PaymentChannel channel)
    {
        var order = await CreateOrderAsync(channel);
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VirtualGrantKeepsTheExactPreviousMembershipAndLeavesOtherProductsUntouched(bool active)
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        var oldOrder = new PaymentOrder { UserId = order.UserId, VipProductId = order.VipProductId,
            OutTradeNo = "OLDER-PURCHASE", Description = "Previous purchase", AmountInFen = 19800, Status = PaymentOrderStatus.Paid };
        var prior = new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
            StartTime = DateTime.UtcNow.AddDays(-60), EndTime = DateTime.UtcNow.AddDays(active ? 60 : -30), LastPaymentOrderId = oldOrder.Id };
        var other = new VipMembership { UserId = order.UserId, VipProduct = new VipProduct { Name = "Another course", PriceInFen = 100 },
            StartTime = DateTime.UtcNow.AddDays(-10), EndTime = DateTime.UtcNow.AddDays(100) };
        _db.AddRange(oldOrder, prior, other); await _db.SaveChangesAsync();
        var original = new PaymentEntitlementState(prior.StartTime, prior.EndTime, prior.LastPaymentOrderId);
        var otherEnd = other.EndTime;
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _service.CompleteAsync(order, "confirmed");
            await transaction.CommitAsync();
        }
        _db.ChangeTracker.Clear();
        var receipt = ReadReceipt(await _db.PaymentLogs.SingleAsync());
        var saved = await _db.VipMemberships.SingleAsync(v => v.Id == prior.Id);
        Assert.AreEqual(original, receipt.Before);
        Assert.AreEqual(new PaymentEntitlementState(saved.StartTime, saved.EndTime, order.Id), receipt.After);
        Assert.AreEqual(prior.Id, receipt.MembershipId);
        if (active) Assert.AreEqual(original.EndTime.AddDays(30), saved.EndTime);
        else Assert.AreEqual(30, (saved.EndTime - saved.StartTime).TotalDays);
        Assert.AreEqual(otherEnd, (await _db.VipMemberships.SingleAsync(v => v.Id == other.Id)).EndTime);
        // Future catalog edits cannot change the receipt of this purchase.
        (await _db.VipProducts.FindAsync(order.VipProductId))!.DurationDays = 900;
        await _db.SaveChangesAsync();
        Assert.AreEqual(receipt, ReadReceipt(await _db.PaymentLogs.SingleAsync()));
    }

    [TestMethod]
    public async Task VirtualReceiptUsesPersistedTimestampPrecisionRatherThanTrackedValues()
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        // Simulate storage precision loss; real MySQL datetime(6) also has fewer
        // fractional digits than a .NET DateTime. Evidence must match the database.
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER round_membership AFTER INSERT ON VipMemberships BEGIN UPDATE VipMemberships SET StartTime = strftime('%Y-%m-%d %H:%M:%f', NEW.StartTime), EndTime = strftime('%Y-%m-%d %H:%M:%f', NEW.EndTime) WHERE Id = NEW.Id; END;");
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, "confirmed"); await transaction.CommitAsync();
        var receipt = ReadReceipt(await _db.PaymentLogs.SingleAsync());
        var stored = await _db.VipMemberships.AsNoTracking().SingleAsync();
        Assert.AreEqual(stored.StartTime, receipt.After.StartTime); Assert.AreEqual(stored.EndTime, receipt.After.EndTime);
        Assert.AreEqual(0, receipt.After.EndTime.Ticks % TimeSpan.TicksPerMillisecond);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task VirtualReceiptFailureRollsBackPaymentMembershipCouponAndAccountingTogether(bool hasGift)
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        var giftEnd = DateTime.UtcNow.AddDays(45);
        if (hasGift)
        {
            _db.Add(new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
                StartTime = DateTime.UtcNow.AddDays(-1), EndTime = giftEnd });
            await _db.SaveChangesAsync();
        }
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_receipt BEFORE INSERT ON PaymentLogs WHEN json_type(NEW.RawData, '$.entitlement') IS NOT NULL BEGIN SELECT RAISE(ABORT, 'Synthetic receipt failure'); END;");
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => _service.CompleteAsync(order, "confirmed"));
            await transaction.RollbackAsync();
        }
        _db.ChangeTracker.Clear();
        order = await _db.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status); Assert.IsNull(order.PaidAt);
        Assert.AreEqual(hasGift ? 1 : 0, await _db.VipMemberships.CountAsync());
        if (hasGift)
        {
            var savedGift = await _db.VipMemberships.SingleAsync();
            Assert.AreEqual(giftEnd, savedGift.EndTime); Assert.IsNull(savedGift.LastPaymentOrderId);
        }
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync()); Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.IsFalse((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_receipt;");
        await using var retry = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, "confirmed"); await retry.CommitAsync();
        Assert.AreEqual(order.Id, ReadReceipt(await _db.PaymentLogs.SingleAsync()).After.LastPaymentOrderId);
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync()); Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task ZeroAmountVirtualGrantStillRecordsItsEntitlementAndDoesNotRestoreTheCoupon()
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.AmountInFen, 0).SetProperty(o => o.DiscountInFen, 19800));
        _db.ChangeTracker.Clear(); order = await _db.PaymentOrders.SingleAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, null); await transaction.CommitAsync();
        Assert.AreEqual(order.Id, ReadReceipt(await _db.PaymentLogs.SingleAsync()).After.LastPaymentOrderId);
        Assert.AreEqual(ChangeType.VipActivatedViaCoupon, (await _db.Changes.SingleAsync()).Type);
        Assert.AreEqual(order.UserId, (await _db.Coupons.SingleAsync()).UsedByUserId);
    }

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi)]
    [DataRow(PaymentChannel.WeChatVirtual)]
    public async Task MissingEntitlementTargetCannotProduceAVirtualPaidOrderButKeepsLegacyBehavior(PaymentChannel channel)
    {
        var order = await CreateOrderAsync(channel);
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.VipProductId, (Guid?)null));
        _db.ChangeTracker.Clear(); order = await _db.PaymentOrders.SingleAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        if (channel == PaymentChannel.WeChatVirtual)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.CompleteAsync(order, "confirmed"));
            await transaction.RollbackAsync(); _db.ChangeTracker.Clear();
            Assert.AreEqual(PaymentOrderStatus.Pending, (await _db.PaymentOrders.SingleAsync()).Status);
            Assert.AreEqual(0, await _db.PaymentLogs.CountAsync());
        }
        else
        {
            await _service.CompleteAsync(order, "confirmed"); await transaction.CommitAsync();
            Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
            using var json = JsonDocument.Parse((await _db.PaymentLogs.SingleAsync()).RawData!);
            Assert.IsFalse(json.RootElement.TryGetProperty("entitlement", out _));
        }
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync()); Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
    }

    private static VirtualPaymentEntitlementReceipt ReadReceipt(PaymentLog log)
    {
        using var json = JsonDocument.Parse(log.RawData!);
        return json.RootElement.GetProperty("entitlement").Deserialize<VirtualPaymentEntitlementReceipt>()!;
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
