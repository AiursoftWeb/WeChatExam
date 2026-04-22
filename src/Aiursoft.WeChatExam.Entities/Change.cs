using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

public enum ChangeType
{
    /// <summary>
    /// 通过微信支付激活（全额或部分支付）
    /// </summary>
    VipActivatedViaPayment = 1,

    /// <summary>
    /// 通过 100% 折扣优惠券激活
    /// </summary>
    VipActivatedViaCoupon = 2,

    /// <summary>
    /// 管理员手动激活
    /// </summary>
    VipActivatedViaAdmin = 3
}

public class Change
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();
    
    public ChangeType Type { get; set; }
    
    /// <summary>
    /// 触发操作的用户（例如管理员 ID，或者用户自己）
    /// </summary>
    [MaxLength(450)]
    public string? TriggerUserId { get; set; }
    
    [ForeignKey(nameof(TriggerUserId))]
    public User? TriggerUser { get; set; }
    
    /// <summary>
    /// 目标用户（被激活 VIP 的用户）
    /// </summary>
    [Required]
    [MaxLength(450)]
    public required string TargetUserId { get; set; }
    
    [ForeignKey(nameof(TargetUserId))]
    public User? TargetUser { get; set; }
    
    public Guid? VipProductId { get; set; }
    
    [ForeignKey(nameof(VipProductId))]
    public VipProduct? VipProduct { get; set; }

    public Guid? CouponId { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }
    
    [MaxLength(200)]
    public string Details { get; set; } = string.Empty;
    
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
