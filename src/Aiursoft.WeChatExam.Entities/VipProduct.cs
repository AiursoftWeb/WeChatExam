using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// A course or institution membership, or the existing all-real-exams membership.
/// </summary>
[Index(nameof(TotalCategoryId))]
[Index(nameof(Type))]
[Index(nameof(InstitutionId))]
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
    /// Course entitlement target. Existing course IDs are preserved during migration.
    /// Real-exam membership remains independent of courses.
    /// </summary>
    public Guid? TotalCategoryId { get; set; }

    [ForeignKey(nameof(TotalCategoryId))]
    public TotalCategory? TotalCategory { get; set; }

    /// <summary>
    /// An institution membership covers this school's real papers, independently of courses.
    /// Legacy RealExam products keep their all-school scope and have no institution target.
    /// </summary>
    public Guid? InstitutionId { get; set; }

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }

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
