namespace Aiursoft.WeChatExam.Entities;

/// <summary>A live question selected for a course-owned mock paper, with its own order and score.</summary>
public class MockPaperQuestion
{
    public Guid MockPaperId { get; set; }
    public MockPaper MockPaper { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Order { get; set; }
    public int Score { get; set; } = 10;
}
