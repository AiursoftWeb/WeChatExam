using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Exam";
    }

    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string PaperTitle { get; set; } = string.Empty;

    [Required]
    public DateTime StartTime { get; set; }

    [Required]
    public DateTime EndTime { get; set; }

    [Required]
    [Range(1, 1000)]
    public int DurationMinutes { get; set; }

    public bool IsPublic { get; set; }

    [Required]
    [Range(1, 10)]
    public int AllowedAttempts { get; set; }

    public bool AllowReview { get; set; }

    public DateTime? ShowAnswerAfter { get; set; }
}
