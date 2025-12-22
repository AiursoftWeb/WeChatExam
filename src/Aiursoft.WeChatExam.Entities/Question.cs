using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class Question
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 题目类型：'singleChoice' 或 'fillIn'
    /// </summary>
    [Required]
    [MaxLength(50)]
    public required string Type { get; set; }

    /// <summary>
    /// 题目描述文本
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public required string Text { get; set; }

    /// <summary>
    /// 单选题选项列表，JSON 序列化为字符串数组
    /// 对于填空题，此字段应为空字符串或包含 []
    /// </summary>
    [MaxLength(5000)]
    public string List { get; set; } = string.Empty;

    /// <summary>
    /// 单选题的正确答案
    /// 对于填空题，此字段应为空字符串
    /// </summary>
    [MaxLength(1000)]
    public string SingleCorrect { get; set; } = string.Empty;

    /// <summary>
    /// 填空题的正确答案数组，JSON 序列化为字符串
    /// 对于单选题，此字段应为 null 或空字符串
    /// </summary>
    [MaxLength(5000)]
    public string FillInCorrect { get; set; } = string.Empty;

    /// <summary>
    /// 题目解析
    /// </summary>
    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// 所属分类的 ID
    /// </summary>
    [Required]
    public required Guid CategoryId { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    // 导航引用：Category?, JsonIgnore, ForeignKey, NotNull
    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    [NotNull]
    public Category? Category { get; set; }
}
