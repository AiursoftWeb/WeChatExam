using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.TagsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Tag";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Tag Name")]
    [MaxLength(50, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Taxonomy")]
    public int? TaxonomyId { get; set; }

    [Display(Name = "Is Free")]
    public bool IsFree { get; set; } = true;

    public IEnumerable<SelectListItem> AvailableTaxonomies { get; set; } = new List<SelectListItem>();
}
