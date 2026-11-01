using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// A personal copy of one incorrectly graded answer. Content is captured at submission,
/// never reconstructed from today's question bank or cascade-deleted with its source.
/// </summary>
public class MistakeRecord
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [StringLength(64)]
    public required string UserId { get; init; }

    [ForeignKey(nameof(UserId))]
    [JsonIgnore]
    public User? User { get; init; }

    public MistakeSource Source { get; init; }
    public PracticeType? PracticeType { get; init; }

    // Provenance only: intentionally no navigation properties or foreign keys to content.
    public Guid? SourceQuestionId { get; init; }
    public Guid? SourceQuestionSnapshotId { get; init; }
    public Guid? SourcePaperId { get; init; }
    public Guid? SourcePaperSnapshotId { get; init; }
    public int? SourcePaperVersion { get; init; }

    [MaxLength(200)]
    public string? SourcePaperTitle { get; init; }

    [MaxLength(2000)]
    public required string Content { get; init; }

    public QuestionType QuestionType { get; init; }

    // Multiple copied payloads must not exceed MySQL's combined VARCHAR row limit.
    [Column(TypeName = "text")]
    [MaxLength(5000)]
    public required string Metadata { get; init; }

    public required string UserAnswer { get; init; }

    [MaxLength(5000)]
    public required string StandardAnswer { get; init; }

    [MaxLength(3000)]
    public required string Explanation { get; init; }

    public int Score { get; init; }
    public int MaxScore { get; init; }

    [Column(TypeName = "text")]
    [MaxLength(4000)]
    public required string GradingComment { get; init; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
