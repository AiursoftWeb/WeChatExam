using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 用户刷题历史（错题本数据源）
/// </summary>
public class UserPracticeHistory
{
    /// <summary>
    /// 主键
    /// </summary>
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 用户ID（必填）
    /// </summary>
    [StringLength(64)]
    public required string UserId { get; set; }

    /// <summary>
    /// 关联用户
    /// </summary>
    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// 题目ID（必填）
    /// </summary>
    public required Guid QuestionId { get; set; }

    /// <summary>
    /// 关联题目
    /// </summary>
    [JsonIgnore]
    [ForeignKey(nameof(QuestionId))]
    [NotNull]
    public Question? Question { get; set; }

    /// <summary>
    /// 用户答案
    /// </summary>
    [MaxLength(1024)]
    public required string UserAnswer { get; set; }

    /// <summary>
    /// 是否答对
    /// </summary>
    public required bool IsCorrect { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
