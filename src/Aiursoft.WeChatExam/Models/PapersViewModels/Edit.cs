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

    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int TimeLimit { get; set; }

    public bool IsFree { get; set; }

    public PaperStatus Status { get; set; }

    [Display(Name = "Category")]
    public Guid? SelectedCategoryId { get; set; }

    public IEnumerable<SelectListItem> AvailableCategories { get; set; } = new List<SelectListItem>();

    public List<PaperQuestion> PaperQuestions { get; set; } = new();

    public List<Question> AvailableQuestions { get; set; } = new();
}
