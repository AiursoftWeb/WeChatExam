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
    IOptions<AppSettings> appSettings,
    ILogger<WeChatPayService> logger) : IWeChatPayService
{
    private readonly AppSettings _settings = appSettings.Value;

    public async Task<CreateOrderResult> CreateOrderAsync(
        string userId,
        string openId,
        Guid vipProductId)
    {
        // Look up VipProduct from DB
        var vipProduct = await dbContext.VipProducts
            .Include(v => v.Category)
            .FirstOrDefaultAsync(v => v.Id == vipProductId);

        if (vipProduct == null)
        {
            return new CreateOrderResult
            {
                Success = false,
                ErrorMessage = "VIP 商品不存在"
            };
        }

        if (!vipProduct.IsEnabled)
        {
            return new CreateOrderResult
            {
                Success = false,
                ErrorMessage = "该 VIP 商品已下架"
            };
        }

        var amountInFen = vipProduct.PriceInFen;
        var description = $"{vipProduct.Name} - {vipProduct.Category?.Title ?? "VIP"}";

        // Check if user already has active VIP for this category
        var now = DateTime.UtcNow;
        var existingVip = await dbContext.VipMemberships
            .Include(v => v.VipProduct)
            .FirstOrDefaultAsync(v => 
                v.UserId == userId && 
                v.VipProduct != null && 
                v.VipProduct.CategoryId == vipProduct.CategoryId &&
                v.StartTime <= now && 
                v.EndTime > now);

        if (existingVip != null)
        {
            return new CreateOrderResult
            {
                Success = false,
                ErrorMessage = "该分类 VIP 未到期，到期后可再次购买。"
            };
        }

        // Check for existing pending order for same user/product to avoid creating duplicates
        var existingPending = await dbContext.PaymentOrders
            .FirstOrDefaultAsync(o =>
                o.UserId == userId &&
                o.VipProductId == vipProductId &&
                o.Status == PaymentOrderStatus.Pending &&
                o.ExpiredAt > DateTime.UtcNow);

        if (existingPending != null)
        {
            // Return existing pending order's pay params
            logger.LogInformation("Found existing pending order {OutTradeNo} for user {UserId}", existingPending.OutTradeNo, userId);

            if (!string.IsNullOrEmpty(existingPending.PrepayId))
            {
                var existingParams = await GetJsApiPayParamsAsync(existingPending.PrepayId);
                return new CreateOrderResult
                {
                    Success = true,
                    OutTradeNo = existingPending.OutTradeNo,
                    PrepayId = existingPending.PrepayId,
                    PayParams = existingParams
                };
            }
        }

        var outTradeNo = $"WCE{DateTime.UtcNow:yyyyMMddHHmmss}{Guid.NewGuid():N}"[..32];

        var paySettings = _settings.WeChat.Payment;

        try
        {
            // Create prepay order via WeChat Pay API
            var request = new CreatePayTransactionJsapiRequest
            {
                OutTradeNumber = outTradeNo,
                AppId = _settings.WeChat.AppId,
                MerchantId = paySettings.MchId,
                Description = description,
                NotifyUrl = paySettings.PaymentNotifyUrl,
                Amount = new CreatePayTransactionJsapiRequest.Types.Amount
                {
                    Total = amountInFen,
                    Currency = "CNY"
                },
                Payer = new CreatePayTransactionJsapiRequest.Types.Payer
                {
                    OpenId = openId
                },
                ExpireTime = DateTimeOffset.UtcNow.AddMinutes(30)
            };

            var response = await tenpayClient.ExecuteCreatePayTransactionJsapiAsync(request);

            if (!response.IsSuccessful())
            {
                logger.LogError("WeChat Pay create order failed: {Code} {Message}",
                    response.ErrorCode, response.ErrorMessage);
                return new CreateOrderResult
                {
                    Success = false,
                    ErrorMessage = $"微信支付下单失败: {response.ErrorMessage}"
                };
            }

            // Save order to database
            var order = new PaymentOrder
            {
                OutTradeNo = outTradeNo,
                UserId = userId,
                VipProductId = vipProductId,
                Description = description,
                AmountInFen = amountInFen,
                Status = PaymentOrderStatus.Pending,
                PrepayId = response.PrepayId,
                ExpiredAt = DateTime.UtcNow.AddMinutes(30)
            };

            dbContext.PaymentOrders.Add(order);

            // Add creation log
            dbContext.PaymentLogs.Add(new PaymentLog
            {
                PaymentOrderId = order.Id,
                EventType = "Created",
                RawData = JsonSerializer.Serialize(new
                {
                    outTradeNo,
                    amountInFen,
                    vipProductId,
                    vipProductName = vipProduct.Name,
                    prepayId = response.PrepayId
                })
            });

            await dbContext.SaveChangesAsync();

            // Generate JSAPI pay parameters
            var payParams = await GetJsApiPayParamsAsync(response.PrepayId);

            logger.LogInformation("Created payment order {OutTradeNo} for user {UserId}, product {ProductName}, amount: {Amount}",
                outTradeNo, userId, vipProduct.Name, amountInFen);

            return new CreateOrderResult
            {
                Success = true,
                OutTradeNo = outTradeNo,
                PrepayId = response.PrepayId,
                PayParams = payParams
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create payment order for user {UserId}", userId);
            return new CreateOrderResult
            {
                Success = false,
                ErrorMessage = "创建支付订单异常，请稍后重试"
            };
        }
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
                order.Status = PaymentOrderStatus.Paid;
                order.WechatTransactionId = transactionId;
                order.PaidAt = DateTime.UtcNow;

                // Process VIP business logic
                await ProcessVipActivationAsync(order);

                dbContext.PaymentLogs.Add(new PaymentLog
                {
                    PaymentOrderId = order.Id,
                    EventType = "StatusChanged",
                    RawData = JsonSerializer.Serialize(new { newStatus = "Paid", transactionId })
                });

                logger.LogInformation("Order {OutTradeNo} paid successfully, transaction: {TransactionId}",
                    outTradeNo, transactionId);
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
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle payment notify. Serial: {SerialNumber}", serialNumber);
            return false;
        }
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
            .ThenInclude(p => p!.Category)
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
                v.VipProduct.CategoryId == categoryId &&
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
    }
}
