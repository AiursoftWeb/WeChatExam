using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
// Redundant using removed
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient.Wechat.TenpayV3;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Models;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Events;
using SKIT.FlurlHttpClient.Wechat.TenpayV3.Utilities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 微信支付服务实现
/// </summary>
public class WeChatPayService(
    WechatTenpayClient tenpayClient,
    WeChatExamDbContext dbContext,
    ICouponService couponService,
    ChangeService changeService,
    IOptions<AppSettings> appSettings,
    ILogger<WeChatPayService> logger) : IWeChatPayService
{
    private readonly AppSettings _settings = appSettings.Value;

    public async Task<CreateOrderResult> CreateOrderAsync(
        string userId, string openId, Guid vipProductId, string? couponCode = null)
    {
        // Keep the identity stable even if the database retries an uncertain commit.
        var outTradeNo = $"WCE{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..32];
        try
        {
            var (order, error) = await InPaymentTransactionAsync(() =>
                ReserveOrderAsync(userId, vipProductId, couponCode, outTradeNo));
            if (order == null) return new CreateOrderResult { Success = false, ErrorMessage = error };

            // Commit the reservation BEFORE calling WeChat. A timeout must leave an
            // order we can retry/query, rather than an untracked payable transaction.
            return await InPaymentTransactionAsync(() => ResumeOrderAsync(order.Id, openId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create or resume payment for user {UserId}", userId);
            return new CreateOrderResult { Success = false, ErrorMessage = "Payment could not be confirmed. Please retry the same purchase." };
        }
    }

    private async Task<T> InPaymentTransactionAsync<T>(Func<Task<T>> action)
    {
        var retrying = false;
        return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) dbContext.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(dbContext.Database.IsSqlite()
                    ? System.Data.IsolationLevel.Serializable : System.Data.IsolationLevel.ReadCommitted)
                : null;
            var result = await action();
            await dbContext.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return result;
        });
    }

    private async Task LockBuyerAsync(string userId)
    {
        if (dbContext.Database.IsRelational())
            await dbContext.Users.Where(u => u.Id == userId)
                .ExecuteUpdateAsync(update => update.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp));
    }

    private async Task LockCouponAsync(Guid? couponId)
    {
        if (couponId.HasValue && dbContext.Database.IsRelational())
            await dbContext.Coupons.Where(c => c.Id == couponId.Value)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.IsEnabled, c => c.IsEnabled));
    }

    private async Task LockOrderAsync(PaymentOrder order)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.PaymentOrders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(o => o.Status, o => o.Status));
        await dbContext.Entry(order).ReloadAsync();
    }

    private async Task<(PaymentOrder? Order, string? Error)> ReserveOrderAsync(
        string userId, Guid productId, string? couponCode, string outTradeNo)
    {
        // Database row locks work across processes. An in-process semaphore would
        // still allow two application instances to reserve the same coupon.
        await LockBuyerAsync(userId);
        var retried = await dbContext.PaymentOrders.FirstOrDefaultAsync(o => o.OutTradeNo == outTradeNo);
        if (retried != null) return (retried, null);
        var product = await dbContext.VipProducts.Include(v => v.TotalCategory).Include(v => v.Institution)
            .FirstOrDefaultAsync(v => v.Id == productId);
        if (product is not { IsEnabled: true }) return (null, "The requested VIP product is unavailable.");

        var now = DateTime.UtcNow;
        if (await dbContext.VipMemberships.AnyAsync(v => v.UserId == userId && v.VipProduct != null &&
            ((v.VipProduct.Type == product.Type && v.VipProduct.TotalCategoryId == product.TotalCategoryId &&
              v.VipProduct.InstitutionId == product.InstitutionId) ||
             (product.Type == VipProductType.Institution && v.VipProduct.Type == VipProductType.RealExam)) &&
            v.StartTime <= now && v.EndTime > now))
            return (null, "The requested membership is already active. Purchase again after it expires.");

        var pending = await dbContext.PaymentOrders.Include(o => o.Coupon)
            .Where(o => o.UserId == userId && o.VipProductId == productId &&
                (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.Failed))
            .OrderBy(o => o.CreatedAt).ToListAsync();
        foreach (var old in pending.Where(o => o.ExpiredAt <= now || o.Status == PaymentOrderStatus.Failed))
        {
            await LockCouponAsync(old.CouponId);
            if (!await ConfirmClosedAsync(old)) return (null, "The previous payment is not confirmed closed. Please check its status before purchasing again.");
        }
        var active = pending.Where(o => o.Status == PaymentOrderStatus.Pending).ToList();
        if (active.Count > 1) return (null, "Multiple previous payments are still pending. Please retry after their payment windows close.");
        if (active.Count == 1)
        {
            var existing = active[0];
            await LockCouponAsync(existing.CouponId);
            if (existing.Coupon != null && dbContext.Database.IsRelational()) await dbContext.Entry(existing.Coupon).ReloadAsync();
            if (!string.IsNullOrWhiteSpace(couponCode) && !string.Equals(couponCode, existing.Coupon?.Code, StringComparison.OrdinalIgnoreCase))
                return (null, "A previous payment is pending with a different coupon. Please finish or wait for that payment to close.");
            if (existing.Coupon is { IsSingleUse: true, UsedByUserId: not null })
            {
                await ConfirmClosedAsync(existing);
                return (null, "This coupon has already been used. Please check the previous payment.");
            }
            if (existing.Coupon is { IsSingleUse: true } && !await ClearOtherCouponOrdersAsync(existing.Coupon.Id, existing.Id))
                return (null, "This coupon is reserved by another pending payment.");
            // Continue the saved price/coupon, even if the current catalog changed.
            return (existing, null);
        }

        Coupon? coupon;
        if (!string.IsNullOrWhiteSpace(couponCode))
        {
            var validation = await couponService.ValidateCouponAsync(couponCode, productId, userId);
            if (!validation.IsValid) return (null, validation.ErrorMessage ?? "Invalid coupon.");
            coupon = validation.Coupon;
        }
        else coupon = await couponService.GetBestApplicableCouponAsync(userId, productId);
        if (coupon != null)
        {
            await LockCouponAsync(coupon.Id);
            if (dbContext.Database.IsRelational()) await dbContext.Entry(coupon).ReloadAsync();
            if (coupon.IsSingleUse && !await ClearOtherCouponOrdersAsync(coupon.Id, null))
                return (null, "This coupon is reserved by another pending payment.");
            // A completed payment found during reconciliation may have consumed it.
            var validation = await couponService.ValidateCouponAsync(coupon.Code, productId, userId);
            if (!validation.IsValid) return (null, validation.ErrorMessage ?? "Invalid coupon.");
        }
        var discount = coupon == null ? 0 : Math.Min(product.PriceInFen, coupon.AmountInFen);
        var order = new PaymentOrder
        {
            UserId = userId, VipProductId = productId, OutTradeNo = outTradeNo,
            CouponId = coupon?.Id, DiscountInFen = discount, AmountInFen = product.PriceInFen - discount,
            Description = $"{product.Name} - {product.TotalCategory?.Title ?? product.Institution?.Title ?? "VIP"}",
            ExpiredAt = DateTime.UtcNow.AddMinutes(30)
        };
        dbContext.PaymentOrders.Add(order);
        dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "Created", RawData = JsonSerializer.Serialize(new { outTradeNo, productId, order.AmountInFen, discount }) });
        if (order.AmountInFen == 0)
        {
            // No gateway is involved, but coupon, membership and accounting must
            // still commit together. A failed activation must not consume the coupon.
            order.PrepayId = "FREE_ORDER";
            await ActivatePaidOrderAsync(order, null);
        }
        return (order, null);
    }

    private async Task<bool> ClearOtherCouponOrdersAsync(Guid couponId, Guid? currentOrderId)
    {
        var others = await dbContext.PaymentOrders.Where(o => o.CouponId == couponId && o.Id != currentOrderId &&
            (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.Failed)).OrderBy(o => o.CreatedAt).ToListAsync();
        foreach (var other in others)
        {
            if (other.Status == PaymentOrderStatus.Pending && other.ExpiredAt > DateTime.UtcNow) return false;
            if (!await ConfirmClosedAsync(other)) return false;
        }
        return true;
    }

    private async Task<bool> ConfirmClosedAsync(PaymentOrder order)
    {
        await LockOrderAsync(order);
        if (order.Status == PaymentOrderStatus.Closed) return true;
        if (order.Status is PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded) return false;
        var query = await tenpayClient.ExecuteGetPayTransactionByOutTradeNumberAsync(new GetPayTransactionByOutTradeNumberRequest
        { MerchantId = _settings.WeChat.Payment.MchId, OutTradeNumber = order.OutTradeNo });
        // Errors (including ORDER_NOT_EXIST), timeouts and a local expiry are not
        // confirmation that a potentially in-flight WeChat transaction is closed.
        if (!query.IsSuccessful() || query.OutTradeNumber != order.OutTradeNo ||
            query.MerchantId != _settings.WeChat.Payment.MchId || query.AppId != _settings.WeChat.AppId) return false;
        if (query.TradeState == "SUCCESS")
        {
            if (query.Amount is { Currency: "CNY", Total: var total } && total == order.AmountInFen && !string.IsNullOrEmpty(query.TransactionId) &&
                order.VipProductId.HasValue && await dbContext.VipProducts.AnyAsync(p => p.Id == order.VipProductId))
                await ActivatePaidOrderAsync(order, query.TransactionId);
            return false;
        }
        if (query.TradeState == "NOTPAY")
        {
            var close = await tenpayClient.ExecuteClosePayTransactionAsync(new ClosePayTransactionRequest
            { MerchantId = _settings.WeChat.Payment.MchId, OutTradeNumber = order.OutTradeNo });
            if (!close.IsSuccessful()) return false;
        }
        else if (query.TradeState != "CLOSED") return false;
        order.Status = PaymentOrderStatus.Closed;
        dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "ClosedBeforeRetry", RawData = JsonSerializer.Serialize(new { order.OutTradeNo, query.TradeState }) });
        await dbContext.SaveChangesAsync();
        return true;
    }

    private async Task<CreateOrderResult> ResumeOrderAsync(Guid orderId, string openId)
    {
        var order = await dbContext.PaymentOrders.SingleAsync(o => o.Id == orderId);
        await LockOrderAsync(order);
        if (order.Status == PaymentOrderStatus.Paid && order.AmountInFen == 0)
            return new CreateOrderResult { Success = true, OutTradeNo = order.OutTradeNo, PrepayId = "FREE_ORDER",
                PayParams = new JsApiPayParams { AppId = _settings.WeChat.AppId, Package = "FREE", SignType = "NONE", PaySign = "NONE", NonceStr = "NONE", TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() } };
        if (order.Status != PaymentOrderStatus.Pending || order.ExpiredAt <= DateTime.UtcNow)
            return new CreateOrderResult { Success = false, ErrorMessage = "The payment state has changed. Please check your membership and retry." };
        if (string.IsNullOrEmpty(order.PrepayId))
        {
            var response = await tenpayClient.ExecuteCreatePayTransactionJsapiAsync(new CreatePayTransactionJsapiRequest
            {
                OutTradeNumber = order.OutTradeNo, AppId = _settings.WeChat.AppId, MerchantId = _settings.WeChat.Payment.MchId,
                Description = order.Description, NotifyUrl = _settings.WeChat.Payment.PaymentNotifyUrl,
                Amount = new CreatePayTransactionJsapiRequest.Types.Amount { Total = order.AmountInFen, Currency = "CNY" },
                Payer = new CreatePayTransactionJsapiRequest.Types.Payer { OpenId = openId },
                ExpireTime = new DateTimeOffset(DateTime.SpecifyKind(order.ExpiredAt, DateTimeKind.Utc))
            });
            if (!response.IsSuccessful())
                return new CreateOrderResult { Success = false, ErrorMessage = "WeChat could not create this payment. Please retry the same purchase." };
            order.PrepayId = response.PrepayId;
        }
        var parameters = await GetJsApiPayParamsAsync(order.PrepayId);
        return new CreateOrderResult { Success = true, OutTradeNo = order.OutTradeNo, PrepayId = order.PrepayId, PayParams = parameters };
    }

    private async Task ActivatePaidOrderAsync(PaymentOrder order, string? transactionId)
    {
        order.Status = PaymentOrderStatus.Paid;
        order.WechatTransactionId = transactionId;
        order.PaidAt = DateTime.UtcNow;
        await ProcessVipActivationAsync(order);
        dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "StatusChanged", RawData = JsonSerializer.Serialize(new { newStatus = "Paid", transactionId }) });
    }

    public async Task<JsApiPayParams> GetJsApiPayParamsAsync(string prepayId)
    {
        var paySettings = _settings.WeChat.Payment;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonceStr = Guid.NewGuid().ToString("N");
        var package = $"prepay_id={prepayId}";

        // Generate payment sign using SDK utility
        var signData = $"{_settings.WeChat.AppId}\n{timestamp}\n{nonceStr}\n{package}\n";

        // Read private key and sign
        var privateKeyPem = await File.ReadAllTextAsync(paySettings.PrivateKeyFilePath);
        var paySign = RSAUtility.SignWithSHA256(privateKeyPem, signData);

        var result = new JsApiPayParams
        {
            AppId = _settings.WeChat.AppId,
            TimeStamp = timestamp,
            NonceStr = nonceStr,
            Package = package,
            SignType = "RSA",
            PaySign = paySign!
        };

        return result;
    }

    public async Task<bool> HandlePaymentNotifyAsync(
        string requestBody,
        string signature,
        string timestamp,
        string nonce,
        string serialNumber)
    {
        try
        {
            var retrying = false;
            return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (retrying) dbContext.ChangeTracker.Clear();
                retrying = true;
                return await ApplyPaymentNotifyAsync(requestBody, signature, timestamp, nonce, serialNumber);
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle payment notify. Serial: {SerialNumber}", serialNumber);
            return false;
        }
    }

    private async Task<bool> ApplyPaymentNotifyAsync(
        string requestBody,
        string signature,
        string timestamp,
        string nonce,
        string serialNumber)
    {
        // Verify callback signature using SDK (using positional arguments to avoid parameter name mismatches)
        var isValid = tenpayClient.VerifyEventSignature(timestamp, nonce, requestBody, signature, serialNumber);

        if (!isValid)
        {
            logger.LogWarning("Payment notify signature verification failed");
            return false;
        }

        // Decrypt notification body
        var callbackModel = tenpayClient.DeserializeEvent(requestBody);
        var paymentResource = tenpayClient.DecryptEventResource<TransactionResource>(callbackModel);

        var outTradeNo = paymentResource.OutTradeNumber;
        var transactionId = paymentResource.TransactionId;
        var tradeState = paymentResource.TradeState;

        logger.LogInformation("Received payment notify for order {OutTradeNo}, state: {TradeState}",
            outTradeNo, tradeState);

        // Find order
        var order = await dbContext.PaymentOrders
            .FirstOrDefaultAsync(o => o.OutTradeNo == outTradeNo);

        if (order == null)
        {
            logger.LogWarning("Payment notify for unknown order: {OutTradeNo}", outTradeNo);
            return true; // Return true to avoid WeChat retrying for unknown orders
        }

        // Signature verification proves the sender, not that this payment matches our order.
        // Compare the saved payable amount, not today's product price or payer_total after WeChat discounts.
        if (tradeState == "SUCCESS" &&
            (paymentResource.Amount is not { Currency: "CNY", Total: var total } ||
             total != order.AmountInFen))
        {
            logger.LogWarning("Payment notify amount or currency mismatch for order {OutTradeNo}; expected {ExpectedAmount} CNY",
                outTradeNo, order.AmountInFen);
            return false;
        }

        // A status check alone races with simultaneous notifications. Hold this
        // order's database write lock, then reload the state committed by the
        // previous handler. The self-assignment acquires the lock without changing
        // business data and works with both MySQL and SQLite.
        // Coupon and accounting services save internally, so keep all activation
        // writes in this transaction: a failed notification must remain retryable.
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync()
            : null;
        if (transaction != null)
        {
            await LockBuyerAsync(order.UserId);
            await LockCouponAsync(order.CouponId);
            await dbContext.PaymentOrders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(o => o.Status, o => o.Status));
            await dbContext.Entry(order).ReloadAsync();
        }

        // Idempotent check: if already in final state, skip processing
        if (order.Status is PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded)
        {
            logger.LogInformation("Order {OutTradeNo} already in final state {Status}, skipping",
                outTradeNo, order.Status);

            // Log the duplicate notification
            dbContext.PaymentLogs.Add(new PaymentLog
            {
                PaymentOrderId = order.Id,
                EventType = "DuplicateNotify",
                RawData = requestBody
            });
            await dbContext.SaveChangesAsync();
            if (transaction != null) await transaction.CommitAsync();
            return true;
        }

        // Log the notification
        dbContext.PaymentLogs.Add(new PaymentLog
        {
            PaymentOrderId = order.Id,
            EventType = "Notified",
            RawData = requestBody
        });

        // Update order status based on trade state
        if (tradeState == "SUCCESS")
        {
            await ActivatePaidOrderAsync(order, transactionId);
        }
        else if (tradeState is "CLOSED" or "REVOKED" or "PAYERROR")
        {
            order.Status = tradeState == "PAYERROR" ? PaymentOrderStatus.Failed : PaymentOrderStatus.Closed;

            dbContext.PaymentLogs.Add(new PaymentLog
            {
                PaymentOrderId = order.Id,
                EventType = "StatusChanged",
                RawData = JsonSerializer.Serialize(new { newStatus = order.Status.ToString(), tradeState })
            });
        }

        await dbContext.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();
        return true;
    }

    public async Task<PaymentOrder?> QueryOrderStatusAsync(string outTradeNo)
    {
        return await dbContext.PaymentOrders
            .Include(o => o.User)
            .Include(o => o.VipProduct)
            .FirstOrDefaultAsync(o => o.OutTradeNo == outTradeNo);
    }

    public async Task<List<VipMembership>> GetVipStatusListAsync(string userId)
    {
        return await dbContext.VipMemberships
            .Include(v => v.User)
            .Include(v => v.VipProduct)
            .ThenInclude(p => p!.TotalCategory)
            .Include(v => v.VipProduct)
            .ThenInclude(p => p!.Institution)
            .Where(v => v.UserId == userId)
            .ToListAsync();
    }

    public async Task<bool> HasVipForCategoryAsync(string userId, Guid categoryId)
    {
        return await dbContext.VipMemberships
            .Include(v => v.VipProduct)
            .AnyAsync(v =>
                v.UserId == userId &&
                v.VipProduct != null &&
                v.VipProduct.Type == VipProductType.Category &&
                v.VipProduct.TotalCategoryId == categoryId &&
                v.StartTime <= DateTime.UtcNow &&
                v.EndTime > DateTime.UtcNow);
    }

    /// <summary>
    /// 支付成功后处理 VIP 激活
    /// </summary>
    private async Task ProcessVipActivationAsync(PaymentOrder order)
    {
        if (order.VipProductId == null)
            return;

        var vipProduct = await dbContext.VipProducts.FindAsync(order.VipProductId);
        if (vipProduct == null)
            return;

        var durationDays = vipProduct.DurationDays;
        var now = DateTime.UtcNow;

        var existingVip = await dbContext.VipMemberships
            .FirstOrDefaultAsync(v => v.UserId == order.UserId && v.VipProductId == order.VipProductId);

        if (existingVip == null)
        {
            // New VIP membership for this product
            var vip = new VipMembership
            {
                UserId = order.UserId,
                VipProductId = order.VipProductId.Value,
                StartTime = now,
                EndTime = now.AddDays(durationDays),
                LastPaymentOrderId = order.Id
            };
            dbContext.VipMemberships.Add(vip);

            logger.LogInformation("Activated VIP {ProductName} for user {UserId}, expires {EndTime}",
                vipProduct.Name, order.UserId, vip.EndTime);
        }
        else
        {
            // Re-purchase: re-activate and extend from existing end time if still valid
            existingVip.EndTime = Math.Max(now.Ticks, existingVip.EndTime.Ticks) == existingVip.EndTime.Ticks
                ? existingVip.EndTime.AddDays(durationDays)
                : now.AddDays(durationDays);
                
            existingVip.StartTime = now;
            existingVip.LastPaymentOrderId = order.Id;

            logger.LogInformation("Re-activated VIP {ProductName} for user {UserId}, new expiry {EndTime}",
                vipProduct.Name, order.UserId, existingVip.EndTime);
        }

        // Record coupon usage if applicable
        if (order.CouponId.HasValue)
        {
            await couponService.RecordUsageAsync(order.CouponId.Value, order.UserId, order.Id, order.DiscountInFen);
        }

        // --- NEW: Record Change Event for Accounting ---
        var changeType = (order.AmountInFen == 0 && order.CouponId.HasValue) 
            ? ChangeType.VipActivatedViaCoupon 
            : ChangeType.VipActivatedViaPayment;

        await changeService.RecordChange(
            type: changeType,
            targetUserId: order.UserId,
            triggerUserId: order.UserId, // User triggered their own activation
            vipProductId: order.VipProductId,
            couponId: order.CouponId,
            details: $"Order: {order.OutTradeNo}. Amount: {order.AmountInFen / 100m}元. Discount: {order.DiscountInFen / 100m}元."
        );
    }
}
