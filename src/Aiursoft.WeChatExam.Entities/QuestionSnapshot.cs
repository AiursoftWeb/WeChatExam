using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 题目快照（不可变，复制自题库Question）
/// </summary>
public class QuestionSnapshot
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 所属试卷快照ID
    /// </summary>
    public required Guid PaperSnapshotId { get; set; }

    [ForeignKey(nameof(PaperSnapshotId))]
    [JsonIgnore]
    [NotNull]
    public PaperSnapshot? PaperSnapshot { get; set; }

    /// <summary>
    /// 原题目ID（可能为null，若原题已删除）
    /// Null 表示原题目已从题库中删除
    /// </summary>
    public Guid? OriginalQuestionId { get; init; }

    /// <summary>
    /// 题目在试卷中的顺序
    /// </summary>
    public int Order { get; init; }

    /// <summary>
    /// 该题目的分值
    /// </summary>
    public int Score { get; init; }

    /// <summary>
    /// 题干内容（快照时复制）
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public required string Content { get; init; }

    /// <summary>
    /// 题目展示类型
    /// </summary>
    [Required]
    public QuestionType QuestionType { get; init; }

    /// <summary>
    /// 判卷方法
    /// </summary>
    [Required]
    public GradingStrategy GradingStrategy { get; init; }

    /// <summary>
    /// 题的Metadata
    /// </summary>
    [MaxLength(5000)]
    public string Metadata { get; init; } = string.Empty;

    /// <summary>
    /// 判卷标准/正确答案
    /// </summary>
    [MaxLength(5000)]
    public string StandardAnswer { get; init; } = string.Empty;

    /// <summary>
    /// 题目解析
    /// </summary>
    [MaxLength(3000)]
    public string Explanation { get; init; } = string.Empty;
}
