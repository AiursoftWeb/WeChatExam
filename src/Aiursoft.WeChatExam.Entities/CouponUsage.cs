using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 优惠码使用记录
/// </summary>
public class CouponUsage
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 关联的优惠码 ID
    /// </summary>
    public Guid CouponId { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }

    /// <summary>
    /// 使用者 ID
    /// </summary>
    [Required]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>
    /// 关联的订单 ID（支付成功后记录）
    /// </summary>
    public Guid PaymentOrderId { get; set; }

    [ForeignKey(nameof(PaymentOrderId))]
    public PaymentOrder? PaymentOrder { get; set; }

    /// <summary>
    /// 使用时间
    /// </summary>
    public DateTime UsedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 实际抵扣金额（单位：分）
    /// </summary>
    public int DiscountInFen { get; set; }
}
