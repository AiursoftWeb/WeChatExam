using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aiursoft.WeChatExam.Models.RealPapersViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel() => PageTitle = "Edit Real Paper";

    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Paper Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Institution")]
    public Guid? InstitutionId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Time Limit (minutes)")]
    public int TimeLimit { get; set; } = 180;

    [BindNever]
    public List<Institution> Institutions { get; set; } = [];
}
