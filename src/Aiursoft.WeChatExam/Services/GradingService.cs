using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public class GradingService : IGradingService, IScopedDependency
{
    private readonly IOllamaService _ollamaService;

    public GradingService(IOllamaService ollamaService)
    {
        _ollamaService = ollamaService;
    }

    public async Task<GradingResult> GradeAsync(Question question, string userAnswer)
    {
        return await GradeAsync(userAnswer, question.StandardAnswer, question.GradingStrategy, 10, question.Content);
    }

    public async Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore, string content)
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
                return await GradeAiEvalAsync(userAnswer, standardAnswer, maxScore, content);
                
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

    private async Task<GradingResult> GradeAiEvalAsync(string userAnswer, string standardAnswer, int maxScore, string content)
    {
        if (string.IsNullOrWhiteSpace(userAnswer))
        {
            return new GradingResult
            {
                IsCorrect = false,
                Score = 0,
                Comment = "No answer provided."
            };
        }

        var prompt = $@"You are an exam grader. Grade the following student's answer based on the standard answer and the question content.

Question: {content}
Standard Answer: {standardAnswer}
Student Answer: {userAnswer}

Provide the score (0-{maxScore}) and a short comment. Output JSON format: {{ ""Score"": 10, ""Comment"": ""..."", ""IsCorrect"": true }}";

        try
        {
            var response = await _ollamaService.AskQuestion(prompt);
            // Simple parsing of AI response. AI might wrap it in markdown or something.
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var result = Newtonsoft.Json.JsonConvert.DeserializeObject<GradingResult>(json);
                if (result != null)
                {
                    return result;
                }
            }
            throw new Exception("Failed to parse AI response as JSON: " + response);
        }
        catch (Exception ex)
        {
            return new GradingResult
            {
                IsCorrect = false,
                Score = 0,
                Comment = "AI grading failed: " + ex.Message
            };
        }
    }
}
