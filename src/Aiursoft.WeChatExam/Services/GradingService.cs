using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public class GradingService : IGradingService, IScopedDependency
{
    public async Task<GradingResult> GradeAsync(Question question, string userAnswer)
    {
        // Use the question's score or a default? 
        // The original implementation returned fixed 10 or 0.
        // We will assume 10 for practice history context if not specified elsewhere.
        return await GradeAsync(userAnswer, question.StandardAnswer, question.GradingStrategy, 10);
    }

    public async Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore)
    {
        userAnswer = userAnswer.Trim();
        standardAnswer = standardAnswer.Trim();

        switch (strategy)
        {
            case GradingStrategy.ExactMatch:
                return GradeExactMatch(userAnswer, standardAnswer, maxScore);
                
            case GradingStrategy.FuzzyMatch:
                return GradeFuzzyMatch(userAnswer, standardAnswer, maxScore);
                
            case GradingStrategy.AiEval:
                return await GradeAiEvalAsync(userAnswer, standardAnswer, maxScore);
                
            default:
                return GradeExactMatch(userAnswer, standardAnswer, maxScore);
        }
    }

    private GradingResult GradeExactMatch(string userAnswer, string standardAnswer, int maxScore)
    {
        var isCorrect = string.Equals(userAnswer, standardAnswer, StringComparison.OrdinalIgnoreCase);
        return new GradingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? maxScore : 0
        };
    }

    private GradingResult GradeFuzzyMatch(string userAnswer, string standardAnswer, int maxScore)
    {
        var isCorrect = userAnswer.Contains(standardAnswer, StringComparison.OrdinalIgnoreCase);
        return new GradingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? maxScore : 0
        };
    }

    private Task<GradingResult> GradeAiEvalAsync(string userAnswer, string standardAnswer, int maxScore)
    {
        // Stub implementation
        var isCorrect = !string.IsNullOrWhiteSpace(userAnswer);
        
        return Task.FromResult(new GradingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? (int)(maxScore * 0.8) : 0,
            Comment = "AI grading stub used."
        });
    }
}
