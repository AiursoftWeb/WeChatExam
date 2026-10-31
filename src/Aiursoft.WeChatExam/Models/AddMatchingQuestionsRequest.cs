using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models;

public class AddMatchingQuestionsRequest
{
    [Required(ErrorMessage = "Review the filtered exam questions before adding them.")]
    [StringLength(64, MinimumLength = 64)]
    [Display(Name = "Selection confirmation")]
    public string MatchFingerprint { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "The {0} must be between {1} and {2}.")]
    [Display(Name = "Score per question")]
    public int Score { get; set; } = 10;
}
