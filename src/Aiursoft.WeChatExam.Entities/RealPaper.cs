using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// A real exam template belongs to one independent institution. Its questions may span courses;
/// institution entitlement covers the whole paper, without granting question-bank membership.
/// Unlike a mock paper, it has no course ownership or anonymous opening setting.
/// </summary>
public class RealPaper
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    public Guid InstitutionId { get; set; }
    public Institution Institution { get; set; } = null!;

    /// <summary>Exam duration in minutes.</summary>
    public int TimeLimit { get; set; } = 180;

    public PaperStatus Status { get; set; } = PaperStatus.Draft;
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
