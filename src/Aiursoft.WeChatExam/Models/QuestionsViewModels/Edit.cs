using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Question";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Question ID")]
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [MaxLength(2000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Content")]
    public string Content { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Question Type")]
    public QuestionType QuestionType { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Grading Strategy")]
    public GradingStrategy GradingStrategy { get; set; }

    [MaxLength(5000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Metadata")]
    public string? Metadata { get; set; }

    [MaxLength(5000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Standard Answer")]
    public string? StandardAnswer { get; set; }

    [MaxLength(3000, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Explanation")]
    public string? Explanation { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Category")]
    public Guid CategoryId { get; set; }

    public List<Category> Categories { get; set; } = new();

    [Display(Name = "Tags")]
    public string? Tags { get; set; }

    public List<string> Options { get; set; } = new();
}
