using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.RealPapersViewModels;

public class UpdateQuestionRequest
{
    public Guid QuestionId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Order")]
    public int Order { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Score")]
    public int Score { get; set; } = 10;
}
