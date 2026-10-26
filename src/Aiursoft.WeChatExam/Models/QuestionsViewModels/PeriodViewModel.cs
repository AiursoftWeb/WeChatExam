using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

public class PeriodViewModel : UiStackLayoutViewModel
{
    public PeriodViewModel()
    {
        PageTitle = "Assign Period";
    }

    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Period")]
    public Guid? QuestionCategoryId { get; set; }

    [BindNever, ValidateNever]
    public string Content { get; set; } = string.Empty;

    [BindNever, ValidateNever]
    public List<QuestionCategory> Periods { get; set; } = [];

    public string? ReturnUrl { get; set; }
}
