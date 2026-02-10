using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Exam";
    }

    [Display(Name = "Exam ID")]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    public string PaperTitle { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Start Time")]
    public DateTime StartTime { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "End Time")]
    public DateTime EndTime { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Range(1, 1000, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Duration (minutes)")]
    public int DurationMinutes { get; set; }

    public bool IsPublic { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Range(1, 10, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Allowed Attempts")]
    public int AllowedAttempts { get; set; }

    public bool AllowReview { get; set; }

    public DateTime? ShowAnswerAfter { get; set; }
}
