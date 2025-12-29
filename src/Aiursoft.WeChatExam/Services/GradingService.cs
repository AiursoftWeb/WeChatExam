using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public class GradingService : IGradingService, IScopedDependency
{
    public async Task<GraingResult> GradeAsync(Question question, string userAnswer)
    {
        // Trim inputs
        userAnswer = userAnswer.Trim();
        var standardAnswer = question.StandardAnswer.Trim();

        switch (question.GradingStrategy)
        {
            case GradingStrategy.ExactMatch:
                return GradeExactMatch(userAnswer, standardAnswer);
                
            case GradingStrategy.FuzzyMatch:
                return GradeFuzzyMatch(userAnswer, standardAnswer);
                
            case GradingStrategy.AiEval:
                return await GradeAiEvalAsync(question.Content, userAnswer, standardAnswer);
                
            default:
                // Fallback to ExactMatch if unknown
                return GradeExactMatch(userAnswer, standardAnswer);
        }
    }

    private GraingResult GradeExactMatch(string userAnswer, string standardAnswer)
    {
        var isCorrect = string.Equals(userAnswer, standardAnswer, StringComparison.OrdinalIgnoreCase);
        return new GraingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? 10 : 0
        };
    }

    private GraingResult GradeFuzzyMatch(string userAnswer, string standardAnswer)
    {
        // Simple contains check: does user answer contain the keyword (StandardAnswer)?
        // Or should it be: does standard answer contain user answer? 
        // Requirement says: user_answer.contains(keyword)
        var isCorrect = userAnswer.Contains(standardAnswer, StringComparison.OrdinalIgnoreCase);
        return new GraingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? 10 : 0
        };
    }

    private Task<GraingResult> GradeAiEvalAsync(string questionContent, string userAnswer, string standardAnswer)
    {
        // TODO: Implement actual AI evaluation
        // For now, return correct if it's not empty, just as a placeholder
        // In real impl, this would call an LLM service
        
        // Stub implementation:
        _ = questionContent;
        _ = standardAnswer;
        var isCorrect = !string.IsNullOrWhiteSpace(userAnswer);
        
        return Task.FromResult(new GraingResult
        {
            IsCorrect = isCorrect,
            Score = isCorrect ? 8 : 0,
            Comment = "AI grading stub used."
        });
    }
}
