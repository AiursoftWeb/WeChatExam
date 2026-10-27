using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aiursoft.WeChatExam.Models.MockPapersViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel() => PageTitle = "Edit Mock Paper";

    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Paper Title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Course")]
    public Guid? TotalCategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Time Limit (minutes)")]
    public int TimeLimit { get; set; } = 180;

    [Display(Name = "Allow exams without membership")]
    public bool AllowViewAnonymousely { get; set; }

    [BindNever]
    public List<TotalCategory> Courses { get; set; } = [];
}
