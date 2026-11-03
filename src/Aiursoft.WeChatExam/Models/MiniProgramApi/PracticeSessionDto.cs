using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class StartPracticeSessionDto
{
    public Guid MistakeId { get; init; }
}

public class PracticeSessionAnswerDto
{
    [Required, MaxLength(1024)] public required string UserAnswer { get; init; }
}

public class ReplacePracticeSessionItemDto
{
    [Required, EnumDataType(typeof(PracticeReplacementReason))]
    public PracticeReplacementReason? Reason { get; init; }
}

public record PracticeSessionDto(Guid Id, Guid SourceMistakeId, DateTime CreationTime,
    int AnsweredCount, bool Completed, IReadOnlyList<PracticeSessionItemDto> Items)
{
    public PracticeSessionKind Kind { get; init; }
}

public record PracticeSessionItemDto(Guid Id, int Position, Guid SourceQuestionId,
    string Availability, string? Content, QuestionType QuestionType, string? Metadata,
    DateTime? SubmittedAt, string? UserAnswer, bool? IsCorrect, int? Score,
    string? StandardAnswer, string? Explanation, string? GradingComment);
