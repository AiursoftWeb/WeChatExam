using System.Text.Json;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.AmountInFen, 0).SetProperty(o => o.DiscountInFen, 19800));
        _db.ChangeTracker.Clear(); var order = await _db.PaymentOrders.SingleAsync();
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
        await CreateOrderAsync(channel);
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.VipProductId, (Guid?)null));
        _db.ChangeTracker.Clear(); var order = await _db.PaymentOrders.SingleAsync();
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

    [TestMethod]
    public async Task ConfirmedFullRefundExpiresOnlyThisGrantOnceAndKeepsItsSpentCoupon()
    {
        var order = await CreateRefundOrderAsync();
        var refund = Refund(order);
        var service = new VirtualPaymentRefundService(_db);
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await service.CompleteAsync(order, refund));
            await transaction.CommitAsync();
        }
        _db.ChangeTracker.Clear(); order = await _db.PaymentOrders.SingleAsync();
        var membership = await _db.VipMemberships.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Refunded, order.Status);
        Assert.IsFalse(membership.IsActive); Assert.AreEqual(membership.StartTime, membership.EndTime);
        Assert.AreEqual(order.Id, membership.LastPaymentOrderId);
        Assert.AreEqual(order.UserId, (await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.IsTrue((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(6800, order.AmountInFen); Assert.AreEqual(13000, order.DiscountInFen);
        await using var repeated = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await service.CompleteAsync(order, refund));
        await _service.CompleteAsync(order, "late-payment");
        await repeated.CommitAsync();
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefunded"));
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FullRefundRestoresThePreviousGiftOrPurchaseInsteadOfDeletingMembership(bool previousPurchase)
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        Guid? previous = null;
        if (previousPurchase)
        {
            var older = new PaymentOrder { UserId = order.UserId, VipProductId = order.VipProductId, OutTradeNo = "ORIGINAL-PAID",
                Description = "Earlier purchase", AmountInFen = 100, Status = PaymentOrderStatus.Paid };
            _db.Add(older); previous = older.Id;
        }
        var gift = new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
            StartTime = DateTime.UtcNow.AddDays(-5), EndTime = DateTime.UtcNow.AddDays(30), LastPaymentOrderId = previous };
        var other = new VipMembership { UserId = order.UserId, VipProduct = new VipProduct { Name = "Other membership" },
            StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(100) };
        _db.AddRange(gift, other); await _db.SaveChangesAsync();
        var before = new PaymentEntitlementState(gift.StartTime, gift.EndTime, previous);
        var otherEnd = other.EndTime;
        await SettleRefundOrderAsync(order);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync(); _db.ChangeTracker.Clear();
        var restored = await _db.VipMemberships.SingleAsync(v => v.Id == gift.Id);
        Assert.AreEqual(before, new PaymentEntitlementState(restored.StartTime, restored.EndTime, restored.LastPaymentOrderId));
        Assert.IsTrue(restored.IsActive);
        Assert.AreEqual(otherEnd, (await _db.VipMemberships.SingleAsync(v => v.Id == other.Id)).EndTime);
    }

    [TestMethod]
    [DataRow("changed")]
    [DataRow("missing")]
    public async Task FullRefundWithChangedOrMissingMembershipRecordsReviewWithoutOverwritingRights(string fault)
    {
        var order = await CreateRefundOrderAsync();
        if (fault == "changed") await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.EndTime, v => v.EndTime.AddDays(10)));
        else await _db.VipMemberships.ExecuteDeleteAsync();
        var before = await _db.VipMemberships.AsNoTracking().Select(v => v.EndTime).ToListAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var service = new VirtualPaymentRefundService(_db);
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await service.CompleteAsync(order, Refund(order)));
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await service.CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync(); _db.ChangeTracker.Clear();
        CollectionAssert.AreEqual(before, await _db.VipMemberships.Select(v => v.EndTime).ToListAsync());
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await _db.PaymentOrders.SingleAsync()).Status);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefundReview"));
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("malformed")]
    [DataRow("version")]
    [DataRow("owner")]
    [DataRow("before-missing")]
    [DataRow("after-null")]
    [DataRow("after-source")]
    public async Task UnusableGrantEvidenceNeverGuessesAwayTheMembership(string fault)
    {
        var order = await CreateRefundOrderAsync();
        var log = await _db.PaymentLogs.SingleAsync(l => l.EventType == "StatusChanged");
        var json = System.Text.Json.Nodes.JsonNode.Parse(log.RawData!)!;
        if (fault == "missing") json.AsObject().Remove("entitlement");
        if (fault == "version") json["entitlement"]!["Version"] = 2;
        if (fault == "owner") json["entitlement"]!["UserId"] = "somebody-else";
        if (fault == "before-missing") json["entitlement"]!.AsObject().Remove("Before");
        if (fault == "after-null") json["entitlement"]!["After"] = null;
        if (fault == "after-source") json["entitlement"]!["After"]!["LastPaymentOrderId"] = Guid.NewGuid();
        var raw = fault == "malformed" ? "not-json" : json.ToJsonString();
        await _db.PaymentLogs.Where(l => l.Id == log.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.RawData, raw));
        var before = (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime;
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync(); _db.ChangeTracker.Clear();
        Assert.AreEqual(before, (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await _db.PaymentOrders.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task PartialRefundIsReviewOnlyAndTheSameRefundIdCannotChangeAmount()
    {
        var order = await CreateRefundOrderAsync(); var before = (await _db.VipMemberships.SingleAsync()).EndTime;
        var refund = Refund(order) with { AmountInFen = 100 };
        await using var transaction = await _db.Database.BeginTransactionAsync();
        var service = new VirtualPaymentRefundService(_db);
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await service.CompleteAsync(order, refund));
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await service.CompleteAsync(order, refund));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync(); _db.ChangeTracker.Clear();
        Assert.AreEqual(before, (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.SingleAsync()).Status);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefundReview"));
    }

    [TestMethod]
    [DataRow("legacy")]
    [DataRow("other-order")]
    [DataRow("other-platform")]
    [DataRow("zero")]
    [DataRow("over-refund")]
    public async Task RefundIdentityMismatchCannotAlterAnOrderOrItsMembership(string fault)
    {
        var order = await CreateRefundOrderAsync();
        if (fault == "legacy")
        {
            await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
            _db.ChangeTracker.Clear(); order = await _db.PaymentOrders.SingleAsync();
        }
        var refund = Refund(order);
        if (fault == "other-order") refund = refund with { PaymentOrderId = Guid.NewGuid() };
        if (fault == "other-platform") refund = refund with { PlatformOrderId = "another-payment" };
        if (fault == "zero") refund = refund with { AmountInFen = 0 };
        if (fault == "over-refund") refund = refund with { AmountInFen = 6801 };
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new VirtualPaymentRefundService(_db).CompleteAsync(order, refund));
        await transaction.RollbackAsync(); _db.ChangeTracker.Clear();
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.SingleAsync()).Status);
        Assert.IsTrue((await _db.VipMemberships.SingleAsync()).IsActive);
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RefundAuditFailureRollsBackMembershipAndOrderAndAllowsSafeRetry(bool manualReview)
    {
        var order = await CreateRefundOrderAsync();
        if (manualReview) await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.EndTime, v => v.EndTime.AddDays(10)));
        var before = (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime;
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_refund BEFORE INSERT ON PaymentLogs WHEN NEW.EventType IN ('VirtualPaymentRefunded', 'VirtualPaymentRefundReview') BEGIN SELECT RAISE(ABORT, 'Synthetic refund audit failure'); END;");
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
            await transaction.RollbackAsync();
        }
        _db.ChangeTracker.Clear(); order = await _db.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
        Assert.AreEqual(before, (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_refund;");
        await using var retry = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(manualReview ? VirtualPaymentRefundOutcome.ReviewRequired : VirtualPaymentRefundOutcome.Completed,
            await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await retry.CommitAsync();
    }

    [TestMethod]
    public async Task RefundSettlementRequiresTheCallersTransaction()
    {
        var order = await CreateRefundOrderAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    public async Task RefundBeforeLocalPaymentSettlementKeepsItsReservationAndMarksReview()
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefundReview"));
    }

    [TestMethod]
    public async Task MissingOriginalPlatformEvidenceCannotCancelAMembership()
    {
        var order = await CreateRefundOrderAsync();
        await _db.PaymentLogs.Where(l => l.EventType == "VirtualPaymentConfirmed").ExecuteDeleteAsync();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync();
        Assert.AreEqual(PaymentOrderStatus.Paid, order.Status);
        Assert.IsTrue((await _db.VipMemberships.AsNoTracking().SingleAsync()).IsActive);
    }

    [TestMethod]
    public async Task ARefundCannotResurrectAnEarlierRefundedMembershipPurchase()
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        var older = new PaymentOrder { UserId = order.UserId, VipProductId = order.VipProductId,
            OutTradeNo = "OLDER-REFUNDED", Description = "Earlier purchase", AmountInFen = 100, Status = PaymentOrderStatus.Paid };
        _db.Add(older);
        _db.Add(new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
            StartTime = DateTime.UtcNow.AddDays(-5), EndTime = DateTime.UtcNow.AddDays(15), LastPaymentOrderId = older.Id });
        await _db.SaveChangesAsync(); await SettleRefundOrderAsync(order);
        await _db.PaymentOrders.Where(o => o.Id == older.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Refunded));
        var currentEnd = (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime;
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired, await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        await transaction.CommitAsync(); _db.ChangeTracker.Clear();
        var membership = await _db.VipMemberships.SingleAsync();
        Assert.AreEqual(order.Id, membership.LastPaymentOrderId); Assert.AreEqual(currentEnd, membership.EndTime);
    }

    [TestMethod]
    public async Task DetachedOrderCannotBeUsedToBypassTheCallersReloadAndLocks()
    {
        var order = await CreateRefundOrderAsync(); _db.ChangeTracker.Clear();
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order)));
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    private static VirtualPaymentRefundConfirmation Refund(PaymentOrder order) => new(order.Id, "original-platform-order", "platform-refund", 6800);

    [TestMethod]
    public async Task IosRefundAdviceUsesCommittedDeliveryFactsWithoutExposingBuyerOrClaimingUsage()
    {
        var order = await CreateAdvisedOrderAsync();
        var before = (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime;
        var service = CreateAdviceService();
        var advice = await service.GetAdviceAsync(Enquiry(order));
        Assert.IsNotNull(advice); Assert.AreEqual(1, advice.ResultCode);
        Assert.Contains("delivered", advice.ResultInfo);
        using var json = JsonDocument.Parse(advice.Evidence);
        Assert.AreEqual("membership_access_granted", json.RootElement.GetProperty("kind").GetString());
        Assert.AreEqual(order.OutTradeNo, json.RootElement.GetProperty("merchantOrder").GetString());
        Assert.AreEqual("original-platform-order", json.RootElement.GetProperty("platformOrder").GetString());
        Assert.AreEqual("original-product", json.RootElement.GetProperty("product").GetString());
        Assert.AreEqual(1, json.RootElement.GetProperty("quantity").GetInt32());
        Assert.AreEqual(before, json.RootElement.GetProperty("accessEndsAt").GetDateTime().ToUniversalTime());
        Assert.DoesNotContain(order.UserId, advice.Evidence);
        Assert.DoesNotContain("saved-private-openid", advice.Evidence);
        Assert.DoesNotContain("CONFIRMED130", advice.Evidence);
        Assert.DoesNotContain("session", advice.Evidence);
        Assert.DoesNotContain("consumed", advice.Evidence);
        Assert.AreEqual(advice, await service.GetAdviceAsync(Enquiry(order)));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.IsTrue((await _db.UserClaimedCoupons.AsNoTracking().SingleAsync()).IsUsed);
        Assert.AreEqual(before, (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime);
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow("empty-order")]
    [DataRow("other-order")]
    [DataRow("platform-order")]
    [DataRow("product")]
    [DataRow("quantity")]
    [DataRow("not-provided")]
    [DataRow("providing")]
    public async Task IosRefundAdviceCannotBindToAnotherOrUndeliveredPurchase(string fault)
    {
        var order = await CreateAdvisedOrderAsync();
        var request = Enquiry(order);
        if (fault == "empty-order") request = request with { PaymentOrderId = Guid.Empty };
        if (fault == "other-order") request = request with { PaymentOrderId = Guid.NewGuid() };
        if (fault == "platform-order") request = request with { PlatformOrderId = "other-payment" };
        if (fault == "product") request = request with { ProductId = "other-product" };
        if (fault == "quantity") request = request with { Quantity = 2 };
        if (fault == "not-provided") request = request with { ProvidedStatus = 0 };
        if (fault == "providing") request = request with { ProvidedStatus = 2 };
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(request));
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Pending)]
    [DataRow(PaymentOrderStatus.Failed)]
    [DataRow(PaymentOrderStatus.Closed)]
    [DataRow(PaymentOrderStatus.Refunded)]
    public async Task IosRefundAdviceDoesNotRefuseOnUnpaidClosedOrRefundedOrders(PaymentOrderStatus status)
    {
        var order = await CreateAdvisedOrderAsync();
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
        Assert.AreEqual(status, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow("snapshot")]
    [DataRow("application")]
    [DataRow("payment-proof")]
    [DataRow("duplicate-payment")]
    [DataRow("grant-proof")]
    [DataRow("malformed-grant")]
    [DataRow("wrong-owner")]
    [DataRow("unknown-version")]
    [DataRow("missing-before")]
    public async Task IosRefundAdviceLeavesIncompleteOrContradictoryEvidenceUndecided(string fault)
    {
        var order = await CreateAdvisedOrderAsync();
        if (fault == "snapshot") await _db.VirtualPaymentSnapshots.ExecuteDeleteAsync();
        if (fault == "application") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(v => v.AppId, "other-app"));
        if (fault == "payment-proof") await _db.PaymentLogs.Where(l => l.EventType == "VirtualPaymentConfirmed").ExecuteDeleteAsync();
        if (fault == "duplicate-payment")
        {
            var raw = (await _db.PaymentLogs.AsNoTracking().SingleAsync(l => l.EventType == "VirtualPaymentConfirmed")).RawData;
            _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentConfirmed", RawData = raw });
            await _db.SaveChangesAsync(); _db.ChangeTracker.Clear();
        }
        if (fault == "grant-proof") await _db.PaymentLogs.Where(l => l.EventType == "StatusChanged").ExecuteDeleteAsync();
        if (fault is "malformed-grant" or "wrong-owner" or "unknown-version" or "missing-before")
        {
            var raw = (await _db.PaymentLogs.AsNoTracking().SingleAsync(l => l.EventType == "StatusChanged")).RawData!;
            var node = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
            if (fault == "wrong-owner") node["entitlement"]!["UserId"] = "different-buyer";
            if (fault == "unknown-version") node["entitlement"]!["Version"] = 99;
            if (fault == "missing-before") node["entitlement"]!.AsObject().Remove("Before");
            raw = fault == "malformed-grant" ? "not-json" : node.ToJsonString();
            await _db.PaymentLogs.Where(l => l.EventType == "StatusChanged").ExecuteUpdateAsync(s => s.SetProperty(l => l.RawData, raw));
        }
        var count = await _db.PaymentLogs.CountAsync();
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
        Assert.AreEqual(count, await _db.PaymentLogs.CountAsync());
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow("gift")]
    [DataRow("later-purchase")]
    [DataRow("missing")]
    public async Task IosRefundAdviceDoesNotAttributeChangedMembershipToOriginalPurchase(string fault)
    {
        var order = await CreateAdvisedOrderAsync();
        if (fault == "gift") await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.EndTime, DateTime.UtcNow.AddYears(2)));
        if (fault == "later-purchase") await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.LastPaymentOrderId, (Guid?)null));
        if (fault == "missing") await _db.VipMemberships.ExecuteDeleteAsync();
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task IosRefundAdviceRequiresCurrentlyActiveAccessEvenWhenReceiptMatches(bool future)
    {
        var order = await CreateAdvisedOrderAsync();
        var start = DateTime.UtcNow.AddDays(future ? 1 : -2);
        var end = start.AddDays(1);
        await _db.VipMemberships.ExecuteUpdateAsync(s => s.SetProperty(v => v.StartTime, start).SetProperty(v => v.EndTime, end));
        var log = await _db.PaymentLogs.AsNoTracking().SingleAsync(l => l.EventType == "StatusChanged");
        var node = System.Text.Json.Nodes.JsonNode.Parse(log.RawData!)!;
        node["entitlement"]!["After"]!["StartTime"] = start;
        node["entitlement"]!["After"]!["EndTime"] = end;
        var raw = node.ToJsonString();
        await _db.PaymentLogs.Where(l => l.Id == log.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.RawData, raw));
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
    }

    [TestMethod]
    public async Task IosRefundAdviceIgnoresNewCatalogButNotUncommittedOrOrdinaryPayments()
    {
        var order = await CreateAdvisedOrderAsync();
        await _db.VipProducts.ExecuteUpdateAsync(s => s.SetProperty(v => v.DurationDays, 999).SetProperty(v => v.PriceInFen, 1));
        var service = CreateAdviceService(); // Sales, legacy WeChat and AppKeys are all absent/disabled.
        Assert.IsNotNull(await service.GetAdviceAsync(Enquiry(order)));
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            Assert.IsNull(await service.GetAdviceAsync(Enquiry(order)));
            Assert.AreSame(transaction, _db.Database.CurrentTransaction);
        }
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
        Assert.IsNull(await service.GetAdviceAsync(Enquiry(order)));
    }

    [TestMethod]
    public async Task IosRefundAdviceCancellationDoesNotSettleAnOrderOrRefund()
    {
        var order = await CreateAdvisedOrderAsync();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateAdviceService().GetAdviceAsync(Enquiry(order), cancellation.Token));
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(100, VirtualPaymentRefundOutcome.ReviewRequired)]
    [DataRow(6800, VirtualPaymentRefundOutcome.Completed)]
    public async Task IosRefundAdviceCannotOverrideACompletedOrPartiallyRefundedPurchase(int amount, VirtualPaymentRefundOutcome expected)
    {
        var order = await CreateAdvisedOrderAsync();
        Assert.IsNotNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            var current = await _db.PaymentOrders.SingleAsync();
            Assert.AreEqual(expected, await new VirtualPaymentRefundService(_db).CompleteAsync(current,
                new VirtualPaymentRefundConfirmation(order.Id, "original-platform-order", "actual-refund", amount)));
            await transaction.CommitAsync();
        }
        _db.ChangeTracker.Clear();
        // A partial refund keeps Paid and active access; its review record must
        // still prevent an automatic recommendation to refuse the refund.
        Assert.IsNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.IsTrue((await _db.UserClaimedCoupons.AsNoTracking().SingleAsync()).IsUsed);
        Assert.AreEqual(3, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    public async Task PlatformReferenceLookupStillUsesOriginalDeliveryEvidenceAndWritesNothing()
    {
        var order = await CreateAdvisedOrderAsync();
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.PlatformOrderId, "original-platform-order"));
        var service = CreateAdviceService();
        Assert.AreEqual(await service.GetAdviceAsync(Enquiry(order)),
            await service.GetAdviceByPlatformOrderAsync("original-platform-order", "original-product", 1, 1));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("application")]
    [DataRow("case")]
    [DataRow("proof")]
    [DataRow("ordinary")]
    [DataRow("refunded")]
    [DataRow("review")]
    [DataRow("duplicate")]
    public async Task PlatformReferenceIsOnlyALookupAndCannotReplaceOriginalPaymentEvidence(string fault)
    {
        var order = await CreateAdvisedOrderAsync();
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.PlatformOrderId, "original-platform-order"));
        if (fault == "missing") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.PlatformOrderId, (string?)null));
        if (fault == "application") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.AppId, "another-app"));
        if (fault == "proof") await _db.PaymentLogs.Where(l => l.EventType == "VirtualPaymentConfirmed")
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.RawData, "{}"));
        if (fault == "ordinary") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
        if (fault == "refunded") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Refunded));
        if (fault == "review")
        {
            _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentRefundReview" });
            await _db.SaveChangesAsync();
        }
        if (fault == "duplicate")
        {
            var duplicate = new PaymentOrder { OutTradeNo = "DUPLICATE-PLATFORM-REFERENCE", UserId = order.UserId,
                Description = "Ambiguous reference", Channel = PaymentChannel.WeChatVirtual };
            _db.AddRange(duplicate, new VirtualPaymentSnapshot { PaymentOrderId = duplicate.Id, AppId = "original-app",
                OpenId = "other-buyer", SignData = "{}", PlatformOrderId = "original-platform-order" });
            await _db.SaveChangesAsync();
        }
        _db.ChangeTracker.Clear();
        Assert.IsNull(await CreateAdviceService().GetAdviceByPlatformOrderAsync(
            fault == "case" ? "ORIGINAL-PLATFORM-ORDER" : "original-platform-order", "original-product", 1, 1));
        Assert.AreEqual(0, _db.ChangeTracker.Entries().Count());
        if (fault == "missing") Assert.IsNotNull(await CreateAdviceService().GetAdviceAsync(Enquiry(order)));
    }

    [TestMethod]
    public async Task PlatformReferenceAdviceCannotUseUncommittedFactsOrMalformedReferences()
    {
        await CreateAdvisedOrderAsync();
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.PlatformOrderId, "original-platform-order"));
        var service = CreateAdviceService();
        Assert.IsNull(await service.GetAdviceByPlatformOrderAsync("", "original-product", 1, 1));
        Assert.IsNull(await service.GetAdviceByPlatformOrderAsync(new string('x', 129), "original-product", 1, 1));
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.IsNull(await service.GetAdviceByPlatformOrderAsync("original-platform-order", "original-product", 1, 1));
        Assert.AreSame(transaction, _db.Database.CurrentTransaction);
    }

    private VirtualPaymentIosRefundAdviceService CreateAdviceService() => new(_db,
        Options.Create(new AppSettings
        {
            AuthProvider = "Local", Local = new LocalSettings(),
            OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "",
                UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
            WeChatEnabled = false, WeChat = new WeChatSettings
            { AppId = "original-app", VirtualPayment = new() { OfferId = "original-offer" } }
        }));

    private static VirtualPaymentIosRefundEnquiry Enquiry(PaymentOrder order) =>
        new(order.Id, "original-platform-order", "original-product", 1, 1);

    private async Task<PaymentOrder> CreateAdvisedOrderAsync()
    {
        var order = await CreateRefundOrderAsync();
        _db.VirtualPaymentSnapshots.Add(new VirtualPaymentSnapshot
        {
            PaymentOrderId = order.Id, AppId = "original-app", OpenId = "saved-private-openid",
            SignData = JsonSerializer.Serialize(new { offerId = "original-offer", buyQuantity = 1, env = 1, currencyType = "CNY",
                productId = "original-product", goodsPrice = 19800, activitySellingPrice = 6800, outTradeNo = order.OutTradeNo, attach = order.Id.ToString("N") })
        });
        await _db.SaveChangesAsync(); _db.ChangeTracker.Clear(); return order;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EarlyFullRefundConsumesSavedCouponOnceWithoutGrantingOrRemovingAnyMembership(bool failed)
    {
        var order = await CreateEarlyRefundAsync();
        if (failed) { order.Status = PaymentOrderStatus.Failed; await _db.SaveChangesAsync(); }
        var gift = new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
            StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(7) };
        _db.VipMemberships.Add(gift); await _db.SaveChangesAsync();
        var end = gift.EndTime;
        var service = new VirtualPaymentEarlyRefundService(_db, new CouponService(_db));
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await service.CompleteAsync(order, EarlyQuery(order)));
            await tx.CommitAsync();
        }
        Assert.AreEqual(PaymentOrderStatus.Refunded, order.Status); Assert.IsNull(order.PaidAt);
        Assert.IsNull(order.WechatTransactionId);
        Assert.AreEqual(end, (await _db.VipMemberships.AsNoTracking().SingleAsync()).EndTime);
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        var usage = await _db.CouponUsages.SingleAsync();
        Assert.AreEqual(order.Id, usage.PaymentOrderId); Assert.AreEqual(13000, usage.DiscountInFen);
        Assert.IsTrue((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(order.UserId, (await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.AreEqual("original-platform-order", (await _db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync()).PlatformOrderId);
        await using var retry = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await service.CompleteAsync(order, EarlyQuery(order)));
        await retry.CommitAsync();
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefunded"));
        Assert.AreEqual(1, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefundReview"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task EarlyRefundSupportsNoCouponOrAMultiUseCouponWithoutDisablingOtherBuyers(bool multiUse)
    {
        var order = await CreateEarlyRefundAsync();
        if (multiUse)
            await _db.Coupons.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsSingleUse, false));
        else
        {
            order.CouponId = null; order.DiscountInFen = 0; await _db.SaveChangesAsync();
        }
        await using var tx = await _db.Database.BeginTransactionAsync();
        var service = new VirtualPaymentEarlyRefundService(_db, new CouponService(_db));
        Assert.AreEqual(VirtualPaymentRefundOutcome.Completed, await service.CompleteAsync(order, EarlyQuery(order)));
        await tx.CommitAsync();
        Assert.AreEqual(multiUse ? 1 : 0, await _db.CouponUsages.CountAsync());
        Assert.IsNull((await _db.Coupons.AsNoTracking().SingleAsync()).UsedByUserId);
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
    }

    [TestMethod]
    [DataRow("partial")]
    [DataRow("missing-review")]
    [DataRow("multiple-reviews")]
    [DataRow("malformed-review")]
    [DataRow("wrong-platform")]
    [DataRow("wrong-order")]
    [DataRow("wrong-amount")]
    [DataRow("paid-query")]
    [DataRow("closed-query")]
    [DataRow("snapshot-conflict")]
    [DataRow("missing-snapshot")]
    [DataRow("grant-log")]
    [DataRow("payment-log")]
    [DataRow("membership")]
    [DataRow("consumed-coupon")]
    [DataRow("used-claim")]
    [DataRow("paid-order")]
    [DataRow("closed-order")]
    [DataRow("jsapi")]
    public async Task EarlyRefundWithIncompleteOrConflictingEvidenceDoesNotReleaseReservation(string fault)
    {
        var order = await CreateEarlyRefundAsync(fault == "partial" ? 100 : 6800);
        var query = EarlyQuery(order);
        switch (fault)
        {
            case "missing-review": await _db.PaymentLogs.ExecuteDeleteAsync(); break;
            case "multiple-reviews":
                _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentRefundReview" }); break;
            case "malformed-review":
                await _db.PaymentLogs.ExecuteUpdateAsync(s => s.SetProperty(l => l.RawData, "{}")); break;
            case "wrong-platform": query = query with { PlatformOrderId = "ORIGINAL-platform-order" }; break;
            case "wrong-order": query = query with { PaymentOrderId = Guid.NewGuid() }; break;
            case "wrong-amount": query = query with { AmountInFen = 19800 }; break;
            case "paid-query": query = query with { State = VirtualPaymentOrderState.Delivered }; break;
            case "closed-query": query = query with { State = VirtualPaymentOrderState.Closed }; break;
            case "snapshot-conflict":
                await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(v => v.PlatformOrderId, "another-payment")); break;
            case "missing-snapshot": await _db.VirtualPaymentSnapshots.ExecuteDeleteAsync(); break;
            case "grant-log":
                _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "StatusChanged" }); break;
            case "payment-log":
                _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentConfirmed" }); break;
            case "membership":
                _db.VipMemberships.Add(new VipMembership { UserId = order.UserId, VipProductId = order.VipProductId!.Value,
                    LastPaymentOrderId = order.Id, StartTime = DateTime.UtcNow, EndTime = DateTime.UtcNow.AddDays(30) }); break;
            case "consumed-coupon":
                await _db.Coupons.ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedByUserId, order.UserId)); break;
            case "used-claim":
                await _db.UserClaimedCoupons.ExecuteUpdateAsync(s => s.SetProperty(c => c.IsUsed, true)); break;
            case "paid-order": order.Status = PaymentOrderStatus.Paid; order.PaidAt = DateTime.UtcNow; break;
            case "closed-order": order.Status = PaymentOrderStatus.Closed; break;
            case "jsapi":
                await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
                await _db.Entry(order).ReloadAsync(); break;
        }
        await _db.SaveChangesAsync();
        var before = order.Status;
        await using var tx = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired,
            await new VirtualPaymentEarlyRefundService(_db, new CouponService(_db)).CompleteAsync(order, query));
        await tx.CommitAsync();
        Assert.AreEqual(before, order.Status);
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefunded"));
    }

    [TestMethod]
    public async Task EarlyRefundAuditFailureRollsBackCouponAndPlatformReferenceAndCanRetry()
    {
        var order = await CreateEarlyRefundAsync();
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_early_refund BEFORE INSERT ON PaymentLogs WHEN NEW.EventType = 'VirtualPaymentRefunded' BEGIN SELECT RAISE(ABORT, 'Synthetic audit failure'); END;");
        await using (var tx = await _db.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() =>
                new VirtualPaymentEarlyRefundService(_db, new CouponService(_db)).CompleteAsync(order, EarlyQuery(order)));
            await tx.RollbackAsync();
        }
        _db.ChangeTracker.Clear();
        order = await _db.PaymentOrders.SingleAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.IsNull((await _db.VirtualPaymentSnapshots.SingleAsync()).PlatformOrderId);
        Assert.IsNull((await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.IsFalse((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_early_refund;");
        await using var retry = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.Completed,
            await new VirtualPaymentEarlyRefundService(_db, new CouponService(_db)).CompleteAsync(order, EarlyQuery(order)));
        await retry.CommitAsync();
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task EarlyRefundRequiresAnOwnedTransactionAndNoPendingEdits()
    {
        var order = await CreateEarlyRefundAsync();
        var service = new VirtualPaymentEarlyRefundService(_db, new CouponService(_db));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CompleteAsync(order, EarlyQuery(order)));
        await using var tx = await _db.Database.BeginTransactionAsync();
        order.Status = PaymentOrderStatus.Failed;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.CompleteAsync(order, EarlyQuery(order)));
    }

    private async Task<PaymentOrder> CreateEarlyRefundAsync(int amount = 6800)
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        _db.VirtualPaymentSnapshots.Add(new VirtualPaymentSnapshot { PaymentOrderId = order.Id,
            AppId = "test-app", OpenId = "test-openid", SignData = "immutable-purchase" });
        await _db.SaveChangesAsync();
        await using var tx = await _db.Database.BeginTransactionAsync();
        Assert.AreEqual(VirtualPaymentRefundOutcome.ReviewRequired,
            await new VirtualPaymentRefundService(_db).CompleteAsync(order, Refund(order) with { AmountInFen = amount }));
        await tx.CommitAsync();
        return order;
    }

    private static VirtualPaymentQueryResult EarlyQuery(PaymentOrder order) =>
        new(VirtualPaymentOrderState.RefundOrReversal, "original-platform-order", null, order.Id, 6800);

    private async Task<PaymentOrder> CreateRefundOrderAsync()
    {
        var order = await CreateOrderAsync(PaymentChannel.WeChatVirtual);
        await SettleRefundOrderAsync(order);
        return order;
    }

    private async Task SettleRefundOrderAsync(PaymentOrder order)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync();
        await _service.CompleteAsync(order, "confirmed");
        _db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentConfirmed",
            RawData = JsonSerializer.Serialize(new { order.OutTradeNo, PlatformOrderId = "original-platform-order" }) });
        await _db.SaveChangesAsync(); await transaction.CommitAsync();
        _db.ChangeTracker.Clear(); _db.Attach(order);
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
