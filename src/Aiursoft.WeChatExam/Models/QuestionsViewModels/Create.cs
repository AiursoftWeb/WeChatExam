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
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 单选题选项列表，JSON 格式的字符串
    /// </summary>
    public string List { get; set; } = string.Empty;

    /// <summary>
    /// 单选题的正确答案，填空题为空字符串
    /// </summary>
    public string SingleCorrect { get; set; } = string.Empty;

    /// <summary>
    /// 填空题的正确答案数组，JSON 格式的字符串
    /// </summary>
    public string FillInCorrect { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    public List<Category> Categories { get; set; } = new();
}
