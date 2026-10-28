using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.RealPapersViewModels;

public class AddQuestionsRequest
{
    [MinLength(1, ErrorMessage = "Select at least one question.")]
    [MaxLength(200, ErrorMessage = "Select no more than 200 questions at a time.")]
    public Guid[] QuestionIds { get; set; } = [];

    [Range(1, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Score per question")]
    public int Score { get; set; } = 10;
}
