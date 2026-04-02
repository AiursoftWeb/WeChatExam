using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 支付订单
/// </summary>
[Index(nameof(OutTradeNo), IsUnique = true)]
[Index(nameof(UserId))]
[Index(nameof(Status))]
public class PaymentOrder
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 商户订单号（唯一，用于与微信支付对接）
    /// </summary>
    [Required]
    [MaxLength(64)]
    public required string OutTradeNo { get; init; }

    /// <summary>
    /// 关联用户 ID
    /// </summary>
    [Required]
    [MaxLength(450)]
    public required string UserId { get; init; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    /// <summary>
    /// 关联 VIP 商品 ID
    /// </summary>
    public Guid? VipProductId { get; init; }

    [ForeignKey(nameof(VipProductId))]
    public VipProduct? VipProduct { get; set; }

    /// <summary>
    /// 关联的优惠券 ID
    /// </summary>
    public Guid? CouponId { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }

    /// <summary>
    /// 优惠金额（单位：分）
    /// </summary>
    public int DiscountInFen { get; set; }

    /// <summary>
    /// 订单描述
    /// </summary>
    [Required]
    [MaxLength(256)]
    public required string Description { get; init; }

    /// <summary>
    /// 订单金额（单位：分）
    /// </summary>
    public int AmountInFen { get; init; }

    /// <summary>
    /// 订单状态
    /// </summary>
    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Pending;

    /// <summary>
    /// 微信支付交易号（支付成功后返回）
    /// </summary>
    [MaxLength(64)]
    public string? WechatTransactionId { get; set; }

    /// <summary>
    /// 微信支付预支付 ID
    /// </summary>
    [MaxLength(128)]
    public string? PrepayId { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 支付成功时间
    /// </summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>
    /// 订单过期时间
    /// </summary>
    public DateTime ExpiredAt { get; init; } = DateTime.UtcNow.AddMinutes(30);

    /// <summary>
    /// 支付日志
    /// </summary>
    [InverseProperty(nameof(PaymentLog.PaymentOrder))]
    public IEnumerable<PaymentLog> PaymentLogs { get; init; } = new List<PaymentLog>();
}
