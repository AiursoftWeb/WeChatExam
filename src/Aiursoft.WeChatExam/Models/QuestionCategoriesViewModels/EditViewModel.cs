using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aiursoft.WeChatExam.Models.QuestionCategoriesViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Period";
    }

    public Guid Id { get; set; }

    [BindNever, ValidateNever]
    public TotalCategory? SavedTotalCategory { get; set; }

    [BindNever]
    public int QuestionCount { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(255, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Period Name")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Total Category")]
    public Guid? TotalCategoryId { get; set; }

    [Display(Name = "Free Practice")]
    public bool IsFree { get; set; }

    [Display(Name = "Visible to students")]
    public bool IsVisible { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Display Order")]
    public int OrderIndex { get; set; }

    [ValidateNever]
    public List<TotalCategory> TotalCategories { get; set; } = [];
}
