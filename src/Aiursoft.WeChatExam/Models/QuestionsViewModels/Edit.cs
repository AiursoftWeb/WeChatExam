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
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public string List { get; set; } = string.Empty;

    public string SingleCorrect { get; set; } = string.Empty;

    public string FillInCorrect { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    public List<Category> Categories { get; set; } = new();
}
