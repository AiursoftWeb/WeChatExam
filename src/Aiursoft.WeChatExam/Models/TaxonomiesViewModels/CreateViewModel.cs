using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Taxonomy";
    }

    [Required]
    [Display(Name = "Taxonomy Name")]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
