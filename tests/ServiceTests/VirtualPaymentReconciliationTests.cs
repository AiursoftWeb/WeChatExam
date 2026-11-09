using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentReconciliationTests
{
    private SqliteContext _db = null!;
    private MemoryCache _cache = null!;
    private TokenHandler _handler = null!;
    private Query _query = null!;
    private VirtualPaymentReconciliationService _service = null!;
    private PaymentOrder _order = null!;
    private DbContextOptions<SqliteContext> _dbOptions = null!;
    private IOptions<AppSettings> _recovery = null!;
    private VirtualPaymentAccessTokenService _tokens = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _dbOptions = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite($"Data Source=virtual-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=10").Options;
        _db = new SqliteContext(_dbOptions);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _handler = new TokenHandler();
        _query = new Query();
        var user = new User { Id = Guid.NewGuid().ToString(), UserName = "buyer", DisplayName = "Buyer",
            MiniProgramOpenId = "saved-openid", SessionKey = "synthetic-session" };
        var product = new VipProduct { Name = "Course", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = 19800, DurationDays = 30 };
        var coupon = new Coupon { Code = "VIRTUAL130", AmountInFen = 13000, IsSingleUse = true,
            DistributionChannel = new DistributionChannel { Id = Guid.NewGuid(), AgencyName = "Agency" } };
        _order = new PaymentOrder { OutTradeNo = "RECOVER-ORDER", User = user, UserId = user.Id, VipProduct = product,
            Coupon = coupon, DiscountInFen = 13000, AmountInFen = 6800, Description = "Reserved", Channel = PaymentChannel.WeChatVirtual,
            ExpiredAt = DateTime.UtcNow.AddMinutes(30) };
        _db.AddRange(_order, new UserClaimedCoupon { UserId = user.Id, CouponId = coupon.Id });
        await _db.SaveChangesAsync();
        var enabled = Settings(virtualSettings: new VirtualPaymentSettings
        {
            Enabled = true, Environment = 1, OfferId = "offer", SandboxAppKey = "synthetic-key",
            Products = new() { [product.Id.ToString()] = new() { ProductId = "course", PriceInFen = 19800 } }
        });
        // Capture is checkout-only. Recovery below deliberately has no login/session or new-sale switch.
        var captureSettings = Settings(virtualSettings: enabled.Value.WeChat.VirtualPayment, weChatEnabled: true);
        _db.VirtualPaymentSnapshots.Add(new VirtualPaymentParameterService(captureSettings).Capture(_order, user));
        user.SessionKey = null;
        await _db.SaveChangesAsync();
        _recovery = Settings(virtualSettings: new VirtualPaymentSettings { OfferId = "offer", SandboxAppKey = "synthetic-key", AppKey = "synthetic-live-key" });
        _tokens = new VirtualPaymentAccessTokenService(_recovery, new TokenFactory(_handler), _cache);
        _service = CreateService(_db, _query);
        _db.ChangeTracker.Clear();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        _cache.Dispose();
        _handler.Dispose();
        _tokens.Dispose();
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Pending)]
    [DataRow(PaymentOrderStatus.Failed)]
    public async Task VerifiedPaymentRecoversExpiredOrderExactlyOnceWithoutBuyerSession(PaymentOrderStatus status)
    {
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status).SetProperty(o => o.ExpiredAt, DateTime.UtcNow.AddDays(-1)));
        var result = await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        Assert.AreEqual(PaymentOrderStatus.Paid, result!.Status);
        var end = (await _db.VipMemberships.SingleAsync()).EndTime;
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        Assert.AreEqual(1, _query.Calls);
        Assert.AreEqual(1, _handler.Calls);
        Assert.AreEqual(end, (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
        Assert.IsTrue((await _db.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(_order.UserId, (await _db.Coupons.SingleAsync()).UsedByUserId);
        Assert.IsNull(result.WechatTransactionId);
        Assert.Contains("platform-order", (await _db.PaymentLogs.SingleAsync(l => l.EventType == "VirtualPaymentConfirmed")).RawData!);
    }

    [TestMethod]
    public async Task WrongOwnerMissingOrderAndOrdinaryChannelNeverContactGateway()
    {
        Assert.IsNull(await _service.ReconcileAsync(_order.OutTradeNo, "another-user"));
        Assert.IsNull(await _service.ReconcileAsync("missing", _order.UserId));
        Assert.IsNull(await _service.ReconcileAsync(_order.OutTradeNo, null));
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
        Assert.IsNull(await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _query.Calls);
        Assert.AreEqual(0, _handler.Calls);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Paid)]
    [DataRow(PaymentOrderStatus.Refunded)]
    [DataRow(PaymentOrderStatus.Closed)]
    public async Task TerminalOrdersAreNotReactivatedOrQueried(PaymentOrderStatus status)
    {
        await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
        Assert.AreEqual(status, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        Assert.AreEqual(0, _query.Calls);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    [DataRow(VirtualPaymentOrderState.Initializing)]
    [DataRow(VirtualPaymentOrderState.Created)]
    [DataRow(VirtualPaymentOrderState.Closed)]
    [DataRow(VirtualPaymentOrderState.RefundOrReversal)]
    public async Task UnconfirmedOrRefundedPlatformStateDoesNotGrantOrReleaseReservation(VirtualPaymentOrderState state)
    {
        _query.State = state;
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    [DataRow(40001)]
    [DataRow(40014)]
    [DataRow(42001)]
    public async Task RejectedTokenIsReacquiredOnceWithoutForcedRefresh(int code)
    {
        _query.Hook = () => _query.Calls == 1 ? throw new VirtualPaymentGatewayException(code) : Task.CompletedTask;
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        CollectionAssert.AreEqual(new[] { "token-1", "token-2" }, _query.Tokens);
        Assert.DoesNotContain("true", _handler.Body!);
    }

    [TestMethod]
    [DataRow(40001, 2)]
    [DataRow(50000, 1)]
    public async Task GatewayFailureHasBoundedRetriesAndLeavesOrderPending(int code, int expectedCalls)
    {
        _query.Hook = () => throw new VirtualPaymentGatewayException(code);
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        Assert.AreEqual(expectedCalls, _query.Calls);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    [DataRow("amount")]
    [DataRow("channel")]
    [DataRow("snapshot")]
    [DataRow("product")]
    public async Task ChangedPurchaseCannotBeSettledUsingAnEarlierProof(string mutation)
    {
        _query.Hook = async () =>
        {
            if (mutation == "amount") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.AmountInFen, 1));
            if (mutation == "channel") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
            if (mutation == "snapshot") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(o => o.OpenId, "changed"));
            if (mutation == "product") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.VipProductId, (Guid?)null));
        };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.ReconcileAsync(_order.OutTradeNo, _order.UserId));
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    public async Task RefundWhileNetworkQueryIsRunningCannotBeReactivated()
    {
        _query.Hook = async () => { await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Refunded)); };
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    public async Task AccountingFailureRollsEverythingBackAndSameOrderCanRetry()
    {
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_virtual_accounting BEFORE INSERT ON Changes BEGIN SELECT RAISE(ABORT, 'Synthetic accounting failure'); END;");
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => _service.ReconcileAsync(_order.OutTradeNo, _order.UserId));
        _db.ChangeTracker.Clear();
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _db.PaymentOrders.SingleAsync()).Status);
        await AssertNoFinancialEffects();
        await _db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_virtual_accounting;");
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task PhoneDisconnectAfterVerifiedProofStillFinishesTheTransaction()
    {
        using var cancellation = new CancellationTokenSource();
        _query.Hook = cancellation.CancelAsync;
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId, cancellation.Token))!.Status);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task MissingSnapshotAndMalformedPurchaseNeverContactGateway()
    {
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(o => o.SignData, "invalid"));
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        await _db.VirtualPaymentSnapshots.ExecuteDeleteAsync();
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        Assert.AreEqual(0, _query.Calls);
        Assert.AreEqual(0, _handler.Calls);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ResultBindingMustMatchOrderAndAmount(bool wrongOrder)
    {
        _query.WrongOrder = wrongOrder;
        _query.WrongAmount = !wrongOrder;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.ReconcileAsync(_order.OutTradeNo, _order.UserId));
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    public async Task SeparateScopesReceivingTheSamePaidProofCannotDoubleGrant()
    {
        // Independent database contexts model two requests/instances, not an
        // in-process mutex. Both obtain proof before either starts fulfillment.
        var reached = 0;
        var bothQueried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task WaitForBoth()
        {
            if (Interlocked.Increment(ref reached) == 2) bothQueried.SetResult();
            await bothQueried.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        var first = new Query { Hook = WaitForBoth };
        var second = new Query { Hook = WaitForBoth };
        await using var otherDb = new SqliteContext(_dbOptions);
        await Task.WhenAll(CreateService(_db, first).ReconcileAsync(_order.OutTradeNo, _order.UserId),
            CreateService(otherDb, second).ReconcileAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(2, reached);
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.AreEqual(2, await _db.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow(VirtualPaymentOrderState.Delivering, "ordinary-transaction")]
    [DataRow(VirtualPaymentOrderState.Delivered, null)]
    public async Task ConfirmedDeliveryStatesCanRecoverWithoutInventingAnIosTransactionId(VirtualPaymentOrderState state, string? transactionId)
    {
        _query.State = state;
        _query.TransactionId = transactionId;
        var result = await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        Assert.AreEqual(PaymentOrderStatus.Paid, result!.Status);
        Assert.AreEqual(transactionId, result.WechatTransactionId);
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
    }

    [TestMethod]
    public async Task ExistingMembershipIsExtendedOnlyOnce()
    {
        var originalEnd = DateTime.UtcNow.AddDays(10);
        _db.VipMemberships.Add(new VipMembership { UserId = _order.UserId, VipProductId = _order.VipProductId!.Value,
            StartTime = DateTime.UtcNow.AddDays(-20), EndTime = originalEnd });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        Assert.AreEqual(originalEnd.AddDays(30), (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task CredentialFailureLeavesTheReservationUntouched()
    {
        _handler.Fail = true;
        Assert.AreEqual(PaymentOrderStatus.Pending, (await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId))!.Status);
        Assert.AreEqual(0, _query.Calls);
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    public async Task CancellationBeforeProofDoesNotSettleTheOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var requestToken = cancellation.Token;
        Func<Task> cancel = cancellation.CancelAsync;
        _query.Hook = async () => { await cancel(); requestToken.ThrowIfCancellationRequested(); };
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ReconcileAsync(_order.OutTradeNo, _order.UserId, requestToken));
        await AssertNoFinancialEffects();
    }

    [TestMethod]
    public async Task RealSdkUsesServerCredentialAndSavedPurchaseBeforeGrantingMembership()
    {
        _handler.AnswerQuery = true;
        var realQuery = new VirtualPaymentQueryService(_recovery, new TokenFactory(_handler));
        var result = await CreateService(_db, realQuery).ReconcileAsync(_order.OutTradeNo, _order.UserId);
        Assert.AreEqual(PaymentOrderStatus.Paid, result!.Status);
        Assert.AreEqual("sdk-transaction", result.WechatTransactionId);
        Assert.AreEqual(2, _handler.Calls);
        Assert.IsNotNull(_handler.QueryUrl);
        Assert.AreEqual("token-1", Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(_handler.QueryUrl.Query)["access_token"].ToString());
        using var queryBody = System.Text.Json.JsonDocument.Parse(_handler.QueryBody!);
        Assert.AreEqual("saved-openid", queryBody.RootElement.GetProperty("openid").GetString());
        Assert.AreEqual(_order.OutTradeNo, queryBody.RootElement.GetProperty("order_id").GetString());
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task DeliveryFallbackSignsSavedPurchaseOnlyAfterCommittedFulfillment(int environment)
    {
        var saved = await _db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync();
        var purchase = System.Text.Json.Nodes.JsonNode.Parse(saved.SignData)!;
        purchase["env"] = environment;
        var raw = purchase.ToJsonString();
        await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(p => p.SignData, raw));
        var delivery = CreateDelivery();
        Assert.IsFalse(await delivery.ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _handler.Calls);
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        var end = (await _db.VipMemberships.SingleAsync()).EndTime;
        _handler.NotifyObserver = () => Assert.IsNull(_db.Database.CurrentTransaction);
        Assert.IsTrue(await delivery.ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(1, _handler.NotifyCalls);
        Assert.IsNotNull(_handler.NotifyUrl);
        var parameters = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(_handler.NotifyUrl.Query);
        Assert.AreEqual("token-1", parameters["access_token"].ToString());
        var signature = Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(environment == 1 ? "synthetic-key" : "synthetic-live-key"),
            System.Text.Encoding.UTF8.GetBytes("/xpay/notify_provide_goods&" + _handler.NotifyBody)));
        Assert.AreEqual(signature, parameters["pay_sig"].ToString());
        using var body = System.Text.Json.JsonDocument.Parse(_handler.NotifyBody!);
        Assert.AreEqual(environment, body.RootElement.GetProperty("env").GetInt32());
        Assert.AreEqual(_order.OutTradeNo, body.RootElement.GetProperty("order_id").GetString());
        Assert.IsFalse(body.RootElement.TryGetProperty("wx_order_id", out _));
        Assert.AreEqual(end, (await _db.VipMemberships.SingleAsync()).EndTime);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task DeliveryRejectsUncommittedTrackedStateAndAnyOpenTransaction()
    {
        var tracked = await _db.PaymentOrders.SingleAsync();
        tracked.Status = PaymentOrderStatus.Paid;
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _handler.Calls);
        _db.ChangeTracker.Clear();
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        await using var transaction = await _db.Database.BeginTransactionAsync();
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _handler.NotifyCalls);
    }

    [TestMethod]
    public async Task DeliveryRejectsAnAmbientTransactionBeforeAnyDatabaseOrGatewayAccess()
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        using var transaction = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _handler.NotifyCalls);
        transaction.Complete();
    }

    [TestMethod]
    [DataRow(VirtualPaymentOrderState.Delivered, true)]
    [DataRow(VirtualPaymentOrderState.Delivering, true)]
    [DataRow(VirtualPaymentOrderState.Created, false)]
    [DataRow(VirtualPaymentOrderState.Initializing, false)]
    [DataRow(VirtualPaymentOrderState.Closed, false)]
    [DataRow(VirtualPaymentOrderState.RefundOrReversal, false)]
    public async Task DeliveryRechecksRemoteStateAndSkipsAlreadyDelivered(VirtualPaymentOrderState state, bool success)
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        _query.State = state;
        Assert.AreEqual(success, await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(state == VirtualPaymentOrderState.Delivering ? 1 : 0, _handler.NotifyCalls);
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("channel")]
    [DataRow("refund")]
    [DataRow("snapshot")]
    [DataRow("missing-snapshot")]
    [DataRow("amount")]
    public async Task DeliveryCannotAcknowledgeAnUnauthorizedOrInvalidLocalPurchase(string fault)
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        if (fault == "channel") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Channel, PaymentChannel.WeChatJsApi));
        if (fault == "refund") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Refunded));
        if (fault == "snapshot") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(o => o.SignData, "invalid"));
        if (fault == "missing-snapshot") await _db.VirtualPaymentSnapshots.ExecuteDeleteAsync();
        if (fault == "amount") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.AmountInFen, 1));
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, fault == "owner" ? "other-buyer" : _order.UserId));
        Assert.AreEqual(1, _handler.Calls);
        Assert.AreEqual(1, _query.Calls);
        Assert.AreEqual(0, _handler.NotifyCalls);
    }

    [TestMethod]
    [DataRow("network")]
    [DataRow("malformed")]
    [DataRow("missing-code")]
    [DataRow("empty")]
    [DataRow("http")]
    [DataRow("platform")]
    public async Task FailedAcknowledgementDoesNotUndoRightsOrExposeGatewaySecrets(string fault)
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        _handler.NotifyFault = fault;
        var logger = new DeliveryLogger();
        Assert.IsFalse(await CreateDelivery(logger).ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(1, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(1, await _db.Changes.CountAsync());
        Assert.DoesNotContain("sensitive-response", logger.Message);
        Assert.DoesNotContain("token-1", logger.Message);
        Assert.IsNull(logger.Exception);
    }

    [TestMethod]
    [DataRow(40001)]
    [DataRow(40014)]
    [DataRow(42001)]
    public async Task DeliveryReacquiresRejectedCredentialOnceWithoutDoubleGranting(int code)
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        _handler.NotifyError = code;
        _handler.FailOnce = true;
        Assert.IsTrue(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(2, _handler.NotifyCalls);
        Assert.AreEqual(3, _query.Calls);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task DeliveryRetryIsBoundedWhenNewCredentialsAreStillRejected()
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        _handler.NotifyError = 40014;
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(2, _handler.NotifyCalls);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    public async Task UncertainNetworkReplyIsRequeriedBeforeAnotherAcknowledgement()
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        _handler.NotifyFault = "network";
        _handler.NotifyObserver = () => _query.State = VirtualPaymentOrderState.Delivered;
        var delivery = CreateDelivery();
        Assert.IsFalse(await delivery.ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.IsTrue(await delivery.ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(1, _handler.NotifyCalls);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    [TestMethod]
    [DataRow("refund")]
    [DataRow("snapshot")]
    [DataRow("result")]
    public async Task DeliveryRejectsChangesWhileWaitingForRemoteProof(string fault)
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        if (fault == "result") _query.WrongAmount = true;
        _query.Hook = async () =>
        {
            if (fault == "refund") await _db.PaymentOrders.ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, PaymentOrderStatus.Refunded));
            if (fault == "snapshot") await _db.VirtualPaymentSnapshots.ExecuteUpdateAsync(s => s.SetProperty(o => o.OpenId, "changed"));
        };
        Assert.IsFalse(await CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId));
        Assert.AreEqual(0, _handler.NotifyCalls);
    }

    [TestMethod]
    public async Task DeliveryCancellationLeavesCommittedRightsIntact()
    {
        await _service.ReconcileAsync(_order.OutTradeNo, _order.UserId);
        using var cancellation = new CancellationTokenSource();
        var requestToken = cancellation.Token;
        _handler.NotifyObserver = cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateDelivery().ConfirmRecoveryAsync(_order.OutTradeNo, _order.UserId, requestToken));
        Assert.AreEqual(PaymentOrderStatus.Paid, (await _db.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(1, await _db.Changes.CountAsync());
    }

    private VirtualPaymentDeliveryService CreateDelivery(ILogger<VirtualPaymentDeliveryService>? logger = null) =>
        new(_db, _query, _tokens, new TokenFactory(_handler), _recovery, logger ?? NullLogger<VirtualPaymentDeliveryService>.Instance);

    private sealed class DeliveryLogger : ILogger<VirtualPaymentDeliveryService>
    {
        public string Message { get; private set; } = "";
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { Message = formatter(state, exception); Exception = exception; }
    }

    private VirtualPaymentReconciliationService CreateService(SqliteContext db, IVirtualPaymentQueryService query) => new(db, query,
        _tokens,
        new PaymentFulfillmentService(db, new CouponService(db), new ChangeService(db), NullLogger<PaymentFulfillmentService>.Instance),
        _recovery, NullLogger<VirtualPaymentReconciliationService>.Instance);

    private async Task AssertNoFinancialEffects()
    {
        Assert.AreEqual(0, await _db.VipMemberships.CountAsync());
        Assert.AreEqual(0, await _db.CouponUsages.CountAsync());
        Assert.AreEqual(0, await _db.Changes.CountAsync());
        Assert.AreEqual(0, await _db.PaymentLogs.CountAsync());
        Assert.IsFalse((await _db.UserClaimedCoupons.AsNoTracking().SingleAsync()).IsUsed);
        Assert.IsNull((await _db.Coupons.AsNoTracking().SingleAsync()).UsedByUserId);
    }

    private static IOptions<AppSettings> Settings(VirtualPaymentSettings virtualSettings, bool weChatEnabled = false) => Options.Create(new AppSettings
    {
        AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = weChatEnabled,
        OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "",
            UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
        WeChat = new WeChatSettings { AppId = "test-app", AppSecret = "synthetic-app-secret", VirtualPayment = virtualSettings }
    });

    private sealed class TokenFactory(TokenHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TokenHandler : HttpMessageHandler
    {
        public int Calls => _calls;
        public string? Body { get; private set; }
        public bool Fail { get; set; }
        public int NotifyCalls { get; private set; }
        public string? NotifyBody { get; private set; }
        public Uri? NotifyUrl { get; private set; }
        public string? NotifyFault { get; set; }
        public int NotifyError { get; set; }
        public bool FailOnce { get; set; }
        public Action? NotifyObserver { get; set; }
        public bool AnswerQuery { get; set; }
        public Uri? QueryUrl { get; private set; }
        public string? QueryBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (request.RequestUri!.AbsolutePath == "/xpay/notify_provide_goods")
            {
                NotifyCalls++;
                NotifyUrl = request.RequestUri;
                NotifyBody = Body;
                NotifyObserver?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (NotifyFault == "network") throw new HttpRequestException("sensitive-response " + request.RequestUri);
                return new HttpResponseMessage(NotifyFault == "http" ? System.Net.HttpStatusCode.BadGateway : System.Net.HttpStatusCode.OK)
                { Content = new StringContent(NotifyFault == "malformed" ? "sensitive-response" : NotifyFault == "missing-code" ? "{}" : NotifyFault == "empty" ? "" : System.Text.Json.JsonSerializer.Serialize(new
                { errcode = NotifyFault == "platform" ? 50000 : (FailOnce && NotifyCalls > 1 ? 0 : NotifyError), errmsg = "sensitive-response" })) };
            }
            if (AnswerQuery && request.RequestUri!.AbsolutePath == "/xpay/query_order")
            {
                QueryUrl = request.RequestUri;
                QueryBody = Body;
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(
                    "{\"errcode\":0,\"order\":{\"order_id\":\"RECOVER-ORDER\",\"env_type\":2,\"order_type\":0,\"status\":2,\"biz_type\":0,\"order_fee\":6800,\"paid_fee\":6800,\"wx_order_id\":\"sdk-platform\",\"wxpay_order_id\":\"sdk-transaction\"}}") };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Fail
                ? "{\"errcode\":40164,\"errmsg\":\"synthetic-sensitive-error\"}"
                : System.Text.Json.JsonSerializer.Serialize(new { access_token = "token-" + Calls, expires_in = 7200 })) };
        }
        private int _calls;
    }

    private sealed class Query : IVirtualPaymentQueryService
    {
        public int Calls { get; private set; }
        public List<string> Tokens { get; } = [];
        public VirtualPaymentOrderState State { get; set; } = VirtualPaymentOrderState.PaidAwaitingDelivery;
        public Func<Task>? Hook { get; set; }
        public bool WrongOrder { get; set; }
        public bool WrongAmount { get; set; }
        public string? TransactionId { get; set; }
        public async Task<VirtualPaymentQueryResult> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot, string accessToken, CancellationToken cancellationToken = default)
        {
            Calls++;
            Tokens.Add(accessToken);
            if (Hook != null) await Hook();
            return new VirtualPaymentQueryResult(State, "platform-order", TransactionId, WrongOrder ? Guid.NewGuid() : order.Id, WrongAmount ? 1 : order.AmountInFen);
        }
    }
}
