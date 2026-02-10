using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.ArticlesViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Article";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Content")]
    public string Content { get; set; } = string.Empty;
}
