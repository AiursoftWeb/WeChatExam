using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 试卷快照（不可变）
/// </summary>
public class PaperSnapshot
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 源试卷ID（如果源试卷被删除，此字段为空）
    /// </summary>
    public Guid? PaperId { get; set; }

    [ForeignKey(nameof(PaperId))]
    [JsonIgnore]
    public Paper? Paper { get; set; }

    /// <summary>
    /// 快照版本号
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// 试卷标题（快照时复制）
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Title { get; init; }

    /// <summary>
    /// 考试时长（分钟）
    /// </summary>
    public int TimeLimit { get; init; }

    /// <summary>
    /// 是否免费
    /// </summary>
    public bool IsFree { get; init; }

    /// <summary>
    /// 快照创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 题目快照列表
    /// </summary>
    [InverseProperty(nameof(QuestionSnapshot.PaperSnapshot))]
    public IEnumerable<QuestionSnapshot> QuestionSnapshots { get; init; } = new List<QuestionSnapshot>();
}
