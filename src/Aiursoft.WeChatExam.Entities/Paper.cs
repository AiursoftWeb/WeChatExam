using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 试卷模板
/// </summary>
public class Paper
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 试卷标题
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>
    /// 考试时长（分钟）
    /// </summary>
    public int TimeLimit { get; set; }

    /// <summary>
    /// 是否免费
    /// </summary>
    public bool IsFree { get; set; }

    /// <summary>
    /// 试卷状态
    /// </summary>
    public PaperStatus Status { get; set; } = PaperStatus.Draft;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 关联的题目（可变，非冻结状态）
    /// </summary>
    [InverseProperty(nameof(PaperQuestion.Paper))]
    public IEnumerable<PaperQuestion> PaperQuestions { get; init; } = new List<PaperQuestion>();

    /// <summary>
    /// 关联的分类
    /// </summary>
    [InverseProperty(nameof(PaperCategory.Paper))]
    public IEnumerable<PaperCategory> PaperCategories { get; init; } = new List<PaperCategory>();

    /// <summary>
    /// 试卷快照列表
    /// </summary>
    [InverseProperty(nameof(PaperSnapshot.Paper))]
    public IEnumerable<PaperSnapshot> PaperSnapshots { get; init; } = new List<PaperSnapshot>();
}
