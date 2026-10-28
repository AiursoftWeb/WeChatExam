namespace Aiursoft.WeChatExam.Entities;

/// <summary>A live question selected for a institution-owned real paper, with its own order and score.</summary>
public class RealPaperQuestion
{
    public Guid RealPaperId { get; set; }
    public RealPaper RealPaper { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Order { get; set; }
    public int Score { get; set; } = 10;
}
