using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// A mock exam template owned by exactly one course, independent of school exams and tags.
/// Course membership covers this paper. Anonymous access is an additional opening for nonmembers.
/// </summary>
public class MockPaper
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    public Guid TotalCategoryId { get; set; }
    public TotalCategory TotalCategory { get; set; } = null!;

    /// <summary>Exam duration in minutes.</summary>
    public int TimeLimit { get; set; } = 180;

    // Defaults to members only; creating a draft must not silently open paid content.
    public bool AllowViewAnonymousely { get; set; }
    public PaperStatus Status { get; set; } = PaperStatus.Draft;
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
