using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Taxonomy";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Taxonomy ID")]
    public int Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Taxonomy Name")]
    [MaxLength(50, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Category")]
    public Guid? CategoryId { get; set; }

    public List<Category> AvailableCategories { get; set; } = new();
}
