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

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int TimeLimit { get; set; } = 60;

    public bool IsFree { get; set; }

    [Display(Name = "Category")]
    public Guid? SelectedCategoryId { get; set; }

    public IEnumerable<SelectListItem> AvailableCategories { get; set; } = new List<SelectListItem>();
}
