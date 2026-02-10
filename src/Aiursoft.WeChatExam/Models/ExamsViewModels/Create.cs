using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Exam";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Paper")]
    public Guid PaperId { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Start Time")]
    public DateTime StartTime { get; set; } = DateTime.Now.AddSeconds(-DateTime.Now.Second).AddMilliseconds(-DateTime.Now.Millisecond);

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "End Time")]
    public DateTime EndTime { get; set; } = DateTime.Now.AddDays(7).AddSeconds(-DateTime.Now.Second).AddMilliseconds(-DateTime.Now.Millisecond);

    [Display(Name = "Duration (minutes)")]
    public int? DurationMinutes { get; set; }

    public List<Paper> AvailablePapers { get; set; } = new();
}
