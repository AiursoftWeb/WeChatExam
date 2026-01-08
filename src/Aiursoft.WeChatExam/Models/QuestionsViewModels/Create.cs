using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Question";
    }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    [Required]
    public QuestionType QuestionType { get; set; }

    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    /// <summary>
    /// Metadata (Choices, Logic, etc.) - JSON
    /// </summary>
    [MaxLength(5000)]
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Standard Answer / Grading Logic
    /// </summary>
    [MaxLength(5000)]
    public string StandardAnswer { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    public string Tags { get; set; } = string.Empty;

    public List<Category> Categories { get; set; } = new();
}
