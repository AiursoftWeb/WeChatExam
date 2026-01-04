using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Paper";
    }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int TimeLimit { get; set; } = 60;

    public bool IsFree { get; set; }
}
