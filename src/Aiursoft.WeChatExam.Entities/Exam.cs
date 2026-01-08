using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 考试安排
/// </summary>
public class Exam
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 考试名称
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>
    /// 关联的试卷模板ID（如果模板被删除，此字段为空，但Exam因为有Snapshot所以不受影响）
    /// </summary>
    public Guid? PaperId { get; set; }

    [ForeignKey(nameof(PaperId))]
    [JsonIgnore]
    public Paper? Paper { get; set; }

    /// <summary>
    ///  绑定特定的试卷快照版本（如果为空，则使用当前最新版本 - 建议创建时锁定）
    ///  为了保证考试一致性，建议在创建考试时将其锁定到特定Snapshot
    /// </summary>
    public Guid? PaperSnapshotId { get; set; }

    [ForeignKey(nameof(PaperSnapshotId))]
    [JsonIgnore]
    public PaperSnapshot? PaperSnapshot { get; set; }

    /// <summary>
    /// 开始时间（考生可以在此时间后开始考试）
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 截止时间（考生必须在此时间前提交，或系统自动提交）
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 考试时长（分钟），默认为试卷的建议时长（但可覆盖）
    /// </summary>
    public int DurationMinutes { get; set; }

    /// <summary>
    /// 允许尝试次数（默认1）
    /// </summary>
    public int AllowedAttempts { get; set; } = 1;

    /// <summary>
    /// 是否公开（所有学生可见）
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// 何时显示正确答案（Null表示不显示）
    /// </summary>
    public DateTime? ShowAnswerAfter { get; set; }

    /// <summary>
    /// 是否允许学生查看详细评分回顾
    /// </summary>
    public bool AllowReview { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 考试记录列表
    /// </summary>
    [InverseProperty(nameof(ExamRecord.Exam))]
    public IEnumerable<ExamRecord> ExamRecords { get; init; } = new List<ExamRecord>();
}
