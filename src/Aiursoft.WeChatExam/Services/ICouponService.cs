using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface ICouponService
{
    Task<Coupon?> GetByCodeAsync(string code);
    Task<Coupon?> GetByIdAsync(Guid id);
    Task<List<Coupon>> GetByChannelAsync(Guid channelId);
    Task<Coupon> CreateAsync(Guid channelId, string code, int amountInFen, bool isSingleUse, List<Guid>? targetVipProductIds = null);
    Task<(bool IsValid, string? ErrorMessage, Coupon? Coupon)> ValidateCouponAsync(string code, Guid vipProductId, string userId);
    Task RecordUsageAsync(Guid couponId, string userId, Guid paymentOrderId, int discountInFen);
    Task DeleteAsync(Guid id);
    Task<(bool Success, string? ErrorMessage)> ClaimCouponAsync(string userId, string code);
    Task<Coupon?> GetBestApplicableCouponAsync(string userId, Guid vipProductId);
    Task<List<Coupon>> GetMyAvailableCouponsAsync(string userId);
}
