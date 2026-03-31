using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 优惠码/促销码实体
/// </summary>
[Index(nameof(Code), IsUnique = true)]
public class Coupon
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 优惠码内容（如：SAVE30, PROMO2024）
    /// </summary>
    [Required]
    [MaxLength(32)]
    public required string Code { get; init; }

    /// <summary>
    /// 关联的分销渠道 ID
    /// </summary>
    public Guid DistributionChannelId { get; set; }

    [ForeignKey(nameof(DistributionChannelId))]
    public DistributionChannel? DistributionChannel { get; set; }

    /// <summary>
    /// 优惠金额（单位：分）
    /// </summary>
    public int AmountInFen { get; set; }

    /// <summary>
    /// 是否为一次性优惠券（一类优惠券：一次性，培训机构使用；二类优惠券：可重复使用，主播/宣传使用）
    /// </summary>
    public bool IsSingleUse { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 对于一次性优惠券，记录使用者 ID
    /// </summary>
    public string? UsedByUserId { get; set; }

    /// <summary>
    /// 对于一次性优惠券，记录使用时间
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// 关联的可优惠 VIP 商品（若为空则适用于所有商品）
    /// </summary>
    [InverseProperty(nameof(CouponVipProduct.Coupon))]
    public IEnumerable<CouponVipProduct> TargetVipProducts { get; init; } = new List<CouponVipProduct>();

    /// <summary>
    /// 优惠码使用记录
    /// </summary>
    [InverseProperty(nameof(CouponUsage.Coupon))]
    public IEnumerable<CouponUsage> Usages { get; init; } = new List<CouponUsage>();
    }
