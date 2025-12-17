using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.WeChatExam.Entities;

public class Category
{
    [Key]
    [MaxLength(128)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ParentId { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    // 导航属性
    [ForeignKey(nameof(ParentId))]
    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();
}