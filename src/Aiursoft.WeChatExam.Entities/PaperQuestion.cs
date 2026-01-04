using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 试卷模板与题库题目的关联关系（可变）
/// </summary>
public class PaperQuestion
{
    [Key]
    public int Id { get; init; }

    /// <summary>
    /// 试卷ID
    /// </summary>
    public required Guid PaperId { get; set; }

    [ForeignKey(nameof(PaperId))]
    [JsonIgnore]
    [NotNull]
    public Paper? Paper { get; set; }

    /// <summary>
    /// 题目ID
    /// </summary>
    public required Guid QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    [JsonIgnore]
    [NotNull]
    public Question? Question { get; set; }

    /// <summary>
    /// 题目在试卷中的顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 该题目的分值
    /// </summary>
    public int Score { get; set; }
}
