using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class UserPracticeHistoryDto
{
    public Guid Id { get; set; }
    
    [Required]
    public Guid QuestionId { get; set; }
    
    public string UserAnswer { get; set; } = string.Empty;
    
    [Required]
    public bool IsCorrect { get; set; }
    
    public DateTime CreationTime { get; set; }
}

public class CreateUserPracticeHistoryDto
{
    [Required]
    public Guid QuestionId { get; set; }
    
    public string UserAnswer { get; set; } = string.Empty;
    
    [Required]
    public bool IsCorrect { get; set; }
}
