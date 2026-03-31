using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 用户领取的优惠券记录
/// </summary>
public class UserClaimedCoupon
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [Required]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    public Guid CouponId { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }

    public DateTime ClaimedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已在订单中使用
    /// </summary>
    public bool IsUsed { get; set; }
}
