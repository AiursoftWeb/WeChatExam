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

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Paper")]
    public Guid PaperId { get; set; }

    [Required]
    [Display(Name = "Start Time")]
    public DateTime StartTime { get; set; } = DateTime.Now.AddSeconds(-DateTime.Now.Second).AddMilliseconds(-DateTime.Now.Millisecond);

    [Required]
    [Display(Name = "End Time")]
    public DateTime EndTime { get; set; } = DateTime.Now.AddDays(7).AddSeconds(-DateTime.Now.Second).AddMilliseconds(-DateTime.Now.Millisecond);

    [Display(Name = "Duration (minutes)")]
    public int? DurationMinutes { get; set; }

    public List<Paper> AvailablePapers { get; set; } = new();
}
