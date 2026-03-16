using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// VIP 会员资格（每个用户对每个 VIP 商品最多一条记录）
/// </summary>
[Index(nameof(UserId), nameof(VipProductId), IsUnique = true)]
[Index(nameof(UserId))]
public class VipMembership
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

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
    public Guid VipProductId { get; init; }

    [ForeignKey(nameof(VipProductId))]
    public VipProduct? VipProduct { get; set; }

    /// <summary>
    /// VIP 开始时间
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// VIP 到期时间
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 最近一次关联的支付订单 ID
    /// </summary>
    public Guid? LastPaymentOrderId { get; set; }

    [ForeignKey(nameof(LastPaymentOrderId))]
    public PaymentOrder? LastPaymentOrder { get; set; }

    /// <summary>
    /// VIP 是否有效（计算属性，不存储）
    /// </summary>
    [NotMapped]
    public bool IsActive => DateTime.UtcNow >= StartTime && DateTime.UtcNow < EndTime;
}
