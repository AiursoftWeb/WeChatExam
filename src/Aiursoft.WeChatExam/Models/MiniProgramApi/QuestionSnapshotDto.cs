using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class QuestionSnapshotDto
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public int Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType QuestionType { get; set; }
    public string Metadata { get; set; } = string.Empty;
}
