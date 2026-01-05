using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IGradingService
{
    Task<GradingResult> GradeAsync(Question question, string userAnswer);
    Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore);
}

public class GradingResult
{
    public bool IsCorrect { get; set; }
    public int Score { get; set; }
    public string Comment { get; set; } = string.Empty;
}
