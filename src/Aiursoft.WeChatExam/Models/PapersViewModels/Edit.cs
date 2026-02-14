using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Paper";
    }

    [Display(Name = "Paper ID")]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Time Limit (minutes)")]
    public int TimeLimit { get; set; }

    [Display(Name = "Is Free")]
    public bool IsFree { get; set; }

    [Display(Name = "Status")]
    public PaperStatus Status { get; set; }

    [Display(Name = "Category")]
    public Guid? SelectedCategoryId { get; set; }

    public IEnumerable<SelectListItem> AvailableCategories { get; set; } = new List<SelectListItem>();

    public IEnumerable<SelectListItem> QuestionTypeOptions { get; set; } = new List<SelectListItem>();

    public List<PaperQuestion> PaperQuestions { get; set; } = new();
}
