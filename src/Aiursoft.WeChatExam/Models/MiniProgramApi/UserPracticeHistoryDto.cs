using System;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class UserPracticeHistoryDto
{
    public Guid Id { get; set; }
    public Guid QuestionId { get; set; }
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime CreationTime { get; set; }
}

public class CreateUserPracticeHistoryDto
{
    public Guid QuestionId { get; set; }
    public string UserAnswer { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
}
