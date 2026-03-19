using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// VIP 商品（分类 VIP 可关联一个分类，真题 VIP 独立于分类）
/// </summary>
[Index(nameof(CategoryId))]
[Index(nameof(Type))]
public class VipProduct
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 商品名称，如"流行音乐VIP"或"真题VIP"
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>
    /// VIP 商品类型（分类VIP 或 真题VIP）
    /// </summary>
    public VipProductType Type { get; set; } = VipProductType.Category;

    /// <summary>
    /// 关联分类 ID（仅分类VIP需要，真题VIP为null）
    /// </summary>
    public Guid? CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    /// <summary>
    /// 价格（单位：分）
    /// </summary>
    public int PriceInFen { get; set; }

    /// <summary>
    /// VIP 持续天数
    /// </summary>
    public int DurationDays { get; set; }

    /// <summary>
    /// 是否启用（管理员可禁用某个 VIP 商品）
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
