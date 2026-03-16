using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class SubmitFeedbackDto
{
    [Required]
    [MaxLength(500)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Contact { get; set; }

    [Required]
    public FeedbackType Type { get; set; }
}

public class FeedbackResponseDto
{
    public int Id { get; set; }
    public FeedbackType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public FeedbackStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
