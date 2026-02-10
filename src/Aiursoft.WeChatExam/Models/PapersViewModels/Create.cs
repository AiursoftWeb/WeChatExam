using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Paper";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Time Limit (minutes)")]
    public int TimeLimit { get; set; } = 60;

    [Display(Name = "Is Free")]
    public bool IsFree { get; set; }

    [Display(Name = "Category")]
    public Guid? SelectedCategoryId { get; set; }

    public IEnumerable<SelectListItem> AvailableCategories { get; set; } = new List<SelectListItem>();
}
