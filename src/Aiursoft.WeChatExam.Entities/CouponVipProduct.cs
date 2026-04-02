using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 优惠码与 VIP 商品的关联表（多对多）
/// 如果一个优惠码没有关联任何商品，则默认适用于所有商品
/// </summary>
public class CouponVipProduct
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid CouponId { get; set; }

    [ForeignKey(nameof(CouponId))]
    public Coupon? Coupon { get; set; }

    public Guid VipProductId { get; set; }

    [ForeignKey(nameof(VipProductId))]
    public VipProduct? VipProduct { get; set; }
}
