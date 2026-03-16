using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

public class Feedback
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [Required]
    [MaxLength(500)]
    public string Content { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? Contact { get; set; }

    public FeedbackType Type { get; set; } = FeedbackType.Other;

    public FeedbackStatus Status { get; set; } = FeedbackStatus.Pending;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
