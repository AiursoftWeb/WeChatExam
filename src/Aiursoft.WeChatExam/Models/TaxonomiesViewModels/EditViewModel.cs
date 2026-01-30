using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Taxonomy";
    }

    [Required]
    public int Id { get; set; }

    [Required]
    [Display(Name = "Taxonomy Name")]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;
}
