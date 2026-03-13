using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// VIP 商品（每个商品关联一个分类，不同分类的 VIP 是不同的商品）
/// </summary>
[Index(nameof(CategoryId))]
public class VipProduct
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 商品名称，如"流行音乐VIP"
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>
    /// 关联分类 ID
    /// </summary>
    public required Guid CategoryId { get; set; }

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
