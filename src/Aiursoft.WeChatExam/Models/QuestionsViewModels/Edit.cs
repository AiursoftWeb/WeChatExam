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

    [Required]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    [Required]
    public QuestionType QuestionType { get; set; }

    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    [MaxLength(5000)]
    public string Metadata { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string StandardAnswer { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    public List<Category> Categories { get; set; } = new();

    public string? Tags { get; set; } = string.Empty;
}
