using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>A fixed, personal practice set. Source IDs are provenance, not content foreign keys.</summary>
public class PracticeSession
{
    [Key] public Guid Id { get; init; }
    [StringLength(64)] public required string UserId { get; init; }
    [ForeignKey(nameof(UserId)), JsonIgnore] public User? User { get; init; }
    public Guid SourceMistakeId { get; init; }
    // Existing sessions are related-question rounds. Keep zero as their persisted default.
    public PracticeSessionKind Kind { get; init; } = PracticeSessionKind.Related;
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
    // Serializes replacements across different positions without holding a lock during vector search.
    public int ReplacementVersion { get; set; }
    public List<PracticeSessionItem> Items { get; init; } = [];
}

public enum PracticeSessionKind
{
    Related = 0,
    BlindSpot = 1
}

/// <summary>
/// Captures the version actually practiced, including successful answers. A submission and
/// its history/mistake copy commit together; SubmittedAt is never a separately committed claim.
/// </summary>
public class PracticeSessionItem
{
    [Key] public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    [ForeignKey(nameof(SessionId)), JsonIgnore] public PracticeSession? Session { get; init; }
    public int Position { get; init; }
    public Guid SourceQuestionId { get; init; }
    [MaxLength(2000)] public required string Content { get; init; }
    public QuestionType QuestionType { get; init; }
    public GradingStrategy GradingStrategy { get; init; }
    [Column(TypeName = "text"), MaxLength(5000)] public required string Metadata { get; init; }
    [Column(TypeName = "text"), MaxLength(5000)] public required string StandardAnswer { get; init; }
    [Column(TypeName = "text"), MaxLength(3000)] public required string Explanation { get; init; }
    [MaxLength(1024)] public string? UserAnswer { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ReplacedAt { get; set; }
    public PracticeReplacementReason? ReplacementReason { get; set; }
    public bool? IsCorrect { get; set; }
    public int? Score { get; set; }
    [Column(TypeName = "text"), MaxLength(4000)] public string? GradingComment { get; set; }
}

/// <summary>Self-reported replacement feedback, never evidence of a correct answer or mastery.</summary>
public enum PracticeReplacementReason
{
    Familiar = 0,
    NotRelated = 1,
    QuestionProblem = 2
}
