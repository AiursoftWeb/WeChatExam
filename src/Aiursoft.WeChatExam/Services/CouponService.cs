using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class CouponService(WeChatExamDbContext context) : ICouponService
{
    public async Task<Coupon?> GetByCodeAsync(string code)
    {
        return await context.Coupons
            .Include(c => c.DistributionChannel)
            .Include(c => c.TargetVipProducts)
            .FirstOrDefaultAsync(c => c.Code == code);
    }

    public async Task<Coupon?> GetByIdAsync(Guid id)
    {
        return await context.Coupons
            .Include(c => c.DistributionChannel)
            .Include(c => c.TargetVipProducts)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<List<Coupon>> GetByChannelAsync(Guid channelId)
    {
        return await context.Coupons
            .Where(c => c.DistributionChannelId == channelId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<Coupon> CreateAsync(Guid channelId, string code, int amountInFen, bool isSingleUse, List<Guid>? targetVipProductIds = null)
    {
        if (await context.Coupons.AnyAsync(c => c.Code == code))
        {
            throw new InvalidOperationException($"Coupon code {code} already exists.");
        }

        if (amountInFen < 0)
        {
            throw new InvalidOperationException("Discount amount cannot be negative.");
        }

        // Validation: Ensure discount amount does not exceed the price of any applicable products
        if (targetVipProductIds != null && targetVipProductIds.Any())
        {
            var products = await context.VipProducts
                .Where(p => targetVipProductIds.Contains(p.Id))
                .ToListAsync();

            foreach (var product in products)
            {
                if (amountInFen > product.PriceInFen)
                {
                    throw new InvalidOperationException($"Discount amount ({amountInFen / 100.0} CNY) cannot exceed the price of product '{product.Name}' ({product.PriceInFen / 100.0} CNY).");
                }
            }
        }
        else
        {
            // If it's a global coupon, it shouldn't exceed the price of any available enabled products
            var minProductPrice = await context.VipProducts
                .Where(p => p.IsEnabled)
                .MinAsync(p => (int?)p.PriceInFen);

            if (minProductPrice.HasValue && amountInFen > minProductPrice.Value)
            {
                throw new InvalidOperationException($"Global discount amount ({amountInFen / 100.0} CNY) cannot exceed the minimum product price ({minProductPrice.Value / 100.0} CNY).");
            }
        }

        var coupon = new Coupon
        {
            DistributionChannelId = channelId,
            Code = code.ToUpper(),
            AmountInFen = amountInFen,
            IsSingleUse = isSingleUse,
            IsEnabled = true
        };

        context.Coupons.Add(coupon);

        if (targetVipProductIds != null && targetVipProductIds.Any())
        {
            foreach (var productId in targetVipProductIds)
            {
                context.CouponVipProducts.Add(new CouponVipProduct
                {
                    CouponId = coupon.Id,
                    VipProductId = productId
                });
            }
        }

        await context.SaveChangesAsync();
        return coupon;
    }

    public async Task<(bool IsValid, string? ErrorMessage, Coupon? Coupon)> ValidateCouponAsync(string code, Guid vipProductId, string userId)
    {
        var coupon = await GetByCodeAsync(code.ToUpper());

        if (coupon == null)
        {
            return (false, "优惠码无效", null);
        }

        if (!coupon.IsEnabled)
        {
            return (false, "优惠码已禁用", coupon);
        }

        if (coupon.DistributionChannel != null && !coupon.DistributionChannel.IsEnabled)
        {
            return (false, "该分销渠道已关闭", coupon);
        }

        if (coupon.IsSingleUse)
        {
            if (coupon.UsedByUserId != null)
            {
                return (false, "该优惠码已被使用", coupon);
            }
        }

        // Check if user has already used this multi-use coupon
        if (!coupon.IsSingleUse)
        {
            var used = await context.CouponUsages
                .AnyAsync(u => u.CouponId == coupon.Id && u.UserId == userId);
            if (used)
            {
                // Note: One user might be allowed to use one public coupon multiple times for different orders.
            }
        }

        if (coupon.TargetVipProducts.Any())
        {
            if (!coupon.TargetVipProducts.Any(p => p.VipProductId == vipProductId))
            {
                return (false, "该优惠码不适用于此商品", coupon);
            }
        }

        return (true, null, coupon);
    }

    public async Task<(bool Success, string? ErrorMessage)> ClaimCouponAsync(string userId, string code)
    {
        var coupon = await GetByCodeAsync(code.ToUpper());
        if (coupon == null || !coupon.IsEnabled)
        {
            return (false, "优惠码无效或已禁用");
        }

        // --- 补强逻辑开始 ---
        if (coupon.IsSingleUse)
        {
            // 检查该券是否已被使用（无论是谁使用的）
            if (coupon.UsedByUserId != null)
            {
                return (false, "该一次性优惠券已被使用");
            }

            // 检查该券是否已被他人领取（即使还没支付）
            var claimedByOthers = await context.UserClaimedCoupons
                .AnyAsync(c => c.CouponId == coupon.Id && c.UserId != userId);
            if (claimedByOthers)
            {
                return (false, "该一次性优惠券已被他人领取");
            }
        }
        // --- 补强逻辑结束 ---

        // 1. 检查是否需要绑定渠道（如果用户当前没有绑定）
        var bound = await context.UserDistributionChannels.AnyAsync(b => b.UserId == userId);
        if (!bound)
        {
            context.UserDistributionChannels.Add(new UserDistributionChannel
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                DistributionChannelId = coupon.DistributionChannelId
            });
        }

        // 2. 检查该用户是否已领取该优惠券且未使用
        var alreadyClaimed = await context.UserClaimedCoupons
            .AnyAsync(c => c.UserId == userId && c.CouponId == coupon.Id && !c.IsUsed);

        if (alreadyClaimed)
        {
            return (true, null); // 已经领取过，直接成功
        }

        // 3. 记录领取
        context.UserClaimedCoupons.Add(new UserClaimedCoupon
        {
            UserId = userId,
            CouponId = coupon.Id,
            IsUsed = false
        });

        await context.SaveChangesAsync();
        return (true, null);
    }

    public async Task<Coupon?> GetBestApplicableCouponAsync(string userId, Guid vipProductId)
    {
        // 获取用户已领取且未使用的所有券
        var claimed = await context.UserClaimedCoupons
            .Include(c => c.Coupon)
            .ThenInclude(co => co!.TargetVipProducts)
            .Where(c => c.UserId == userId && !c.IsUsed && c.Coupon != null && c.Coupon.IsEnabled)
            .ToListAsync();

        var bestCoupon = claimed
            .Select(c => c.Coupon!)
            .Where(c => !c.TargetVipProducts.Any() || c.TargetVipProducts.Any(tp => tp.VipProductId == vipProductId))
            .OrderByDescending(c => c.AmountInFen) // 优先选择折扣力度最大的
            .FirstOrDefault();

        return bestCoupon;
    }

    public async Task<List<Coupon>> GetMyAvailableCouponsAsync(string userId)
    {
        var claimed = await context.UserClaimedCoupons
            .Include(c => c.Coupon)
            .ThenInclude(co => co!.TargetVipProducts)
            .Where(c => c.UserId == userId && !c.IsUsed && c.Coupon != null && c.Coupon.IsEnabled)
            .ToListAsync();

        return claimed.Select(c => c.Coupon!).ToList();
    }

    public async Task RecordUsageAsync(Guid couponId, string userId, Guid paymentOrderId, int discountInFen)
    {
        var coupon = await context.Coupons.FindAsync(couponId);
        if (coupon == null) return;

        // 1. 标记领取记录为已使用
        var claimRecord = await context.UserClaimedCoupons
            .FirstOrDefaultAsync(c => c.UserId == userId && c.CouponId == couponId && !c.IsUsed);
        if (claimRecord != null)
        {
            claimRecord.IsUsed = true;
        }

        // 2. 记录实际使用统计
        var usage = new CouponUsage
        {
            CouponId = couponId,
            UserId = userId,
            PaymentOrderId = paymentOrderId,
            DiscountInFen = discountInFen
        };

        context.CouponUsages.Add(usage);

        if (coupon.IsSingleUse)
        {
            coupon.UsedByUserId = userId;
            coupon.UsedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var coupon = await context.Coupons.FindAsync(id);
        if (coupon != null)
        {
            context.Coupons.Remove(coupon);
            await context.SaveChangesAsync();
        }
    }
}
