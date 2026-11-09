using System.Data;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Reserves a FIRST virtual purchase. No public route, gateway charge or resume:
/// previous attempts must be reconciled/closed by their own channel separately.
/// Nothing payable escapes before the order and immutable snapshot commit.
/// </summary>
public class VirtualPaymentCheckoutService(WeChatExamDbContext db, ICouponService coupons,
    PaymentFulfillmentService fulfillment, VirtualPaymentParameterService parameters,
    IOptions<AppSettings> options, IHttpClientFactory httpClientFactory) : IScopedDependency
{
    public async Task<VirtualPaymentCheckoutResult> CreateAsync(string userId, Guid productId, string loginCode,
        string? couponCode = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value.WeChat;
        if (!options.Value.WeChatEnabled || !settings.VirtualPayment.Enabled)
            throw new InvalidOperationException("Virtual payment is not enabled.");
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
            throw new InvalidOperationException("Virtual checkout requires its own relational transaction.");
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(loginCode) || loginCode.Length > 512 ||
            string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
            throw new InvalidOperationException("Please sign in again before paying.");
        var buyer = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (buyer == null || string.IsNullOrWhiteSpace(buyer.MiniProgramOpenId))
            throw new InvalidOperationException("Please sign in again before paying.");

        // A JWT or a historical stored session_key is not a fresh payment session.
        // Refresh outside database locks; never persist or return this session_key.
        using (var client = new SessionClient(new WechatApiClientOptions
               { AppId = settings.AppId, AppSecret = settings.AppSecret },
                   httpClientFactory.CreateClient(nameof(VirtualPaymentCheckoutService))))
        {
            SnsJsCode2SessionResponse response;
            try { response = await client.ExecuteSnsJsCode2SessionAsync(new SnsJsCode2SessionRequest { JsCode = loginCode }, cancellationToken); }
            catch (CommonException) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            catch (CommonException) { throw new InvalidOperationException("Payment login could not be verified. Please sign in again."); }
            if (!response.IsSuccessful() || response.OpenId != buyer.MiniProgramOpenId || string.IsNullOrWhiteSpace(response.SessionKey))
                throw new InvalidOperationException("Payment login could not be verified. Please sign in again.");
            buyer.SessionKey = response.SessionKey;
        }

        // Stable across EF execution-strategy retries, including an uncertain commit.
        var id = Guid.NewGuid();
        var number = $"WCV{DateTime.UtcNow:yyyyMMddHHmmss}{id:N}"[..32];
        var retrying = false;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) db.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, cancellationToken);
            // Same cross-process buyer -> coupon -> order lock ordering as old checkout.
            if (await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u =>
                    u.SetProperty(x => x.ConcurrencyStamp, x => x.ConcurrencyStamp), cancellationToken) != 1 ||
                !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.MiniProgramOpenId == buyer.MiniProgramOpenId, cancellationToken))
                throw new InvalidOperationException("The payment identity changed. Please sign in again.");

            var saved = await db.PaymentOrders.Include(o => o.Coupon).SingleOrDefaultAsync(o => o.Id == id, cancellationToken);
            if (saved != null)
            {
                // Only OUR same execution-strategy attempt can reach this path.
                // Never reuse another request's WeChat order number here.
                var oldSnapshot = await db.VirtualPaymentSnapshots.SingleOrDefaultAsync(s => s.PaymentOrderId == id, cancellationToken);
                var retried = ToResult(saved, saved.Status == PaymentOrderStatus.Paid
                    ? null : parameters.Prepare(saved, buyer, oldSnapshot ?? throw new InvalidOperationException("The saved purchase is incomplete.")));
                await transaction.CommitAsync(cancellationToken);
                return retried;
            }

            var product = await db.VipProducts.Include(p => p.TotalCategory).Include(p => p.Institution)
                .SingleOrDefaultAsync(p => p.Id == productId, cancellationToken);
            if (product is not { IsEnabled: true, PriceInFen: >= 0, DurationDays: > 0 } ||
                product.Type is not (VipProductType.Category or VipProductType.Institution) ||
                (product.Type == VipProductType.Category ? product.TotalCategoryId == null : product.InstitutionId == null))
                throw new InvalidOperationException("The requested VIP product is unavailable.");
            var now = DateTime.UtcNow;
            if (await db.VipMemberships.AnyAsync(v => v.UserId == userId && v.VipProduct != null &&
                    v.VipProduct.Type == product.Type && v.VipProduct.TotalCategoryId == product.TotalCategoryId &&
                    v.VipProduct.InstitutionId == product.InstitutionId && v.StartTime <= now && v.EndTime > now, cancellationToken))
                throw new InvalidOperationException("The requested membership is already active. Purchase again after it expires.");
            // Local expiration, Failed and another gateway are NOT proof of closure.
            // This first-reservation service cannot release, resume or close them.
            if (await db.PaymentOrders.AnyAsync(o => o.UserId == userId && o.VipProductId == productId &&
                    (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.Failed), cancellationToken))
                throw new InvalidOperationException("A previous payment is not confirmed closed. Please check its status before purchasing again.");

            Coupon? coupon;
            if (!string.IsNullOrWhiteSpace(couponCode))
            {
                var validation = await coupons.ValidateCouponAsync(couponCode, productId, userId);
                if (!validation.IsValid) throw new InvalidOperationException(validation.ErrorMessage ?? "Invalid coupon.");
                coupon = validation.Coupon;
            }
            else coupon = await coupons.GetBestApplicableCouponAsync(userId, productId);
            if (coupon != null)
            {
                await db.Coupons.Where(c => c.Id == coupon.Id).ExecuteUpdateAsync(c => c.SetProperty(x => x.IsEnabled, x => x.IsEnabled), cancellationToken);
                await db.Entry(coupon).ReloadAsync(cancellationToken);
                if (coupon.IsSingleUse && await db.PaymentOrders.AnyAsync(o => o.CouponId == coupon.Id &&
                        (o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.Failed), cancellationToken))
                    throw new InvalidOperationException("This coupon is reserved by another pending payment.");
                var validation = await coupons.ValidateCouponAsync(coupon.Code, productId, userId);
                if (!validation.IsValid) throw new InvalidOperationException(validation.ErrorMessage ?? "Invalid coupon.");
                if (coupon.AmountInFen < 0) throw new InvalidOperationException("Invalid coupon amount.");
            }
            var discount = coupon == null ? 0 : Math.Min(product.PriceInFen, coupon.AmountInFen);
            var order = new PaymentOrder
            {
                Id = id, OutTradeNo = number, UserId = userId, VipProductId = productId, Channel = PaymentChannel.WeChatVirtual,
                CouponId = coupon?.Id, Coupon = coupon, DiscountInFen = discount, AmountInFen = product.PriceInFen - discount,
                Description = $"{product.Name} - {product.TotalCategory?.Title ?? product.Institution?.Title ?? "VIP"}",
                ExpiredAt = now.AddMinutes(30)
            };
            db.PaymentOrders.Add(order);
            db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = id, EventType = "Created", RawData = JsonSerializer.Serialize(new { number, productId, order.AmountInFen, discount }) });
            VirtualPaymentParameters? pay = null;
            if (order.AmountInFen == 0)
            {
                // A fully discounted purchase never calls requestVirtualPayment.
                // Coupon, entitlement and accounting still commit atomically.
                order.PrepayId = "FREE_ORDER";
                await fulfillment.CompleteAsync(order, null);
            }
            else
            {
                var snapshot = parameters.Capture(order, buyer);
                db.VirtualPaymentSnapshots.Add(snapshot);
                pay = parameters.Prepare(order, buyer, snapshot);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(order, pay);
        });
    }

    private static VirtualPaymentCheckoutResult ToResult(PaymentOrder order, VirtualPaymentParameters? pay) =>
        new(order.OutTradeNo, order.Status, order.AmountInFen, order.DiscountInFen, order.Coupon?.Code.Trim(), pay);

    private sealed class SessionClient(WechatApiClientOptions settings, HttpClient httpClient) : WechatApiClient(settings, httpClient);
}

public sealed record VirtualPaymentCheckoutResult(string OutTradeNo, PaymentOrderStatus Status, int AmountInFen,
    int DiscountInFen, string? AppliedCouponCode, VirtualPaymentParameters? PayParams);
