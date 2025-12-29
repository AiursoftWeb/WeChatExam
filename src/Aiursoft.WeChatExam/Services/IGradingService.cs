using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IGradingService
{
    Task<GraingResult> GradeAsync(Question question, string userAnswer);
}

public class GraingResult
{
    public bool IsCorrect { get; set; }
    public double Score { get; set; } // Can be expanded for partial credit
    public string Comment { get; set; } = string.Empty;
}
