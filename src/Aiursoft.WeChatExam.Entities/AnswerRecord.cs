using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 单个题目的答题记录
/// </summary>
public class AnswerRecord
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 考试记录ID
    /// </summary>
    public required Guid ExamRecordId { get; set; }

    [ForeignKey(nameof(ExamRecordId))]
    [JsonIgnore]
    [NotNull]
    public ExamRecord? ExamRecord { get; set; }

    /// <summary>
    /// 题目快照ID
    /// </summary>
    public required Guid QuestionSnapshotId { get; set; }

    [ForeignKey(nameof(QuestionSnapshotId))]
    [JsonIgnore]
    [NotNull]
    public QuestionSnapshot? QuestionSnapshot { get; set; }

    /// <summary>
    /// 用户答案
    /// </summary>
    [MaxLength(5000)]
    public string UserAnswer { get; set; } = string.Empty;

    /// <summary>
    /// 该题得分
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// 是否已判分
    /// </summary>
    public bool IsMarked { get; set; }

    /// <summary>
    /// 详细判分结果（JSON）
    /// </summary>
    [MaxLength(5000)]
    public string GradingResult { get; set; } = string.Empty;
}
