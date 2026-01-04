using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Paper";
    }

    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int TimeLimit { get; set; }

    public bool IsFree { get; set; }

    public PaperStatus Status { get; set; }

    public List<PaperQuestion> PaperQuestions { get; set; } = new();

    public List<Question> AvailableQuestions { get; set; } = new();
}
