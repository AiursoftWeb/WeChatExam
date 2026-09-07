using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class GetUserPracticeHistoryQueryDto : IValidatableObject
{
    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, int.MaxValue)]
    public int? Count { get; set; }

    public Guid? QuestionId { get; set; }

    public PracticeType? PracticeType { get; set; }

    public QuestionType? QuestionType { get; set; }

    public DateTimeOffset? StartTime { get; set; }

    public DateTimeOffset? EndTime { get; set; }

    public bool? IsCorrect { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PracticeType.HasValue && !Enum.IsDefined(PracticeType.Value))
        {
            yield return new ValidationResult("Invalid practice type.", [nameof(PracticeType)]);
        }

        if (QuestionType.HasValue && !Enum.IsDefined(QuestionType.Value))
        {
            yield return new ValidationResult("Invalid question type.", [nameof(QuestionType)]);
        }

        if (StartTime.HasValue && StartTime.Value.Offset != TimeSpan.Zero)
        {
            yield return new ValidationResult("Start time must be UTC.", [nameof(StartTime)]);
        }

        if (EndTime.HasValue && EndTime.Value.Offset != TimeSpan.Zero)
        {
            yield return new ValidationResult("End time must be UTC.", [nameof(EndTime)]);
        }

        if (StartTime.HasValue && EndTime.HasValue && StartTime.Value > EndTime.Value)
        {
            yield return new ValidationResult(
                "Start time must be earlier than or equal to end time.",
                [nameof(StartTime), nameof(EndTime)]);
        }
    }
}

public class UserPracticeHistoryDto
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid QuestionId { get; set; }
    
    public string UserAnswer { get; set; } = string.Empty;
    
    public string StandardAnswer { get; set; } = string.Empty;
    
    public string Explanation { get; set; } = string.Empty;
    
    [Required]
    public bool IsCorrect { get; set; }
    
    public int? Score { get; set; }

    public PracticeType? PracticeType { get; set; }
    
    public DateTime CreationTime { get; set; }
}

public class CreateUserPracticeHistoryDto
{
    [Required]
    public Guid QuestionId { get; set; }
    
    public string UserAnswer { get; set; } = string.Empty;

    public PracticeType? PracticeType { get; set; }
}