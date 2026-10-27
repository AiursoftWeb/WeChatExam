using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.InstitutionsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Institution";
    }

    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Institution Name")]
    public string Title { get; set; } = string.Empty;

    [Range(0, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Display Order")]
    public int OrderIndex { get; set; }
}
