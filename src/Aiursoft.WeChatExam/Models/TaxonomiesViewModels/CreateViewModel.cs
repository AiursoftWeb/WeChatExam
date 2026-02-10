using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Taxonomy";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Taxonomy Name")]
    [MaxLength(50, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    public string Name { get; set; } = string.Empty;
}
