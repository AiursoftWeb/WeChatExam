using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 学生的考试记录（一次尝试）
/// </summary>
public class ExamRecord
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 考试ID
    /// </summary>
    public required Guid ExamId { get; set; }

    [ForeignKey(nameof(ExamId))]
    [JsonIgnore]
    [NotNull]
    public Exam? Exam { get; set; }

    /// <summary>
    /// 用户ID
    /// </summary>
    [Required]
    public required string UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// 使用的试卷快照ID（确保考试内容一致性）
    /// </summary>
    public required Guid PaperSnapshotId { get; set; }

    [ForeignKey(nameof(PaperSnapshotId))]
    [JsonIgnore]
    [NotNull]
    public PaperSnapshot? PaperSnapshot { get; set; }

    /// <summary>
    /// 尝试次序（第几次考试）
    /// </summary>
    public int AttemptIndex { get; set; }

    /// <summary>
    /// 实际开始时间
    /// </summary>
    public DateTime StartTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 提交时间（未提交则为null）
    /// </summary>
    public DateTime? SubmitTime { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public ExamRecordStatus Status { get; set; } = ExamRecordStatus.InProgress;

    /// <summary>
    /// 总分（判分后更新）
    /// </summary>
    public int TotalScore { get; set; }

    /// <summary>
    /// 教师评语
    /// </summary>
    [MaxLength(1000)]
    public string TeacherComment { get; set; } = string.Empty;

    /// <summary>
    /// 答题记录
    /// </summary>
    [InverseProperty(nameof(AnswerRecord.ExamRecord))]
    public IEnumerable<AnswerRecord> AnswerRecords { get; init; } = new List<AnswerRecord>();
}
