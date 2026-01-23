using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Newtonsoft.Json;

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
        return await GradeAsync(userAnswer, question.StandardAnswer, question.GradingStrategy, 10, question.Content, question.Explanation);
    }

    public Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore, string content)
    {
        return GradeAsync(userAnswer, standardAnswer, strategy, maxScore, content, string.Empty);
    }

    public async Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore, string content, string explanation)
    {
        userAnswer = userAnswer?.Trim() ?? string.Empty;
        standardAnswer = standardAnswer?.Trim() ?? string.Empty;

        switch (strategy)
        {
            case GradingStrategy.ExactMatch:
                return GradeExactMatch(userAnswer, standardAnswer, maxScore);
                
            case GradingStrategy.FuzzyMatch:
                return GradeFuzzyMatch(userAnswer, standardAnswer, maxScore);
                
            case GradingStrategy.AiEval:
                return await GradeAiEvalAsync(userAnswer, standardAnswer, maxScore, content, explanation);
                
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

    private async Task<GradingResult> GradeAiEvalAsync(string userAnswer, string standardAnswer, int maxScore, string content, string explanation)
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
Explanation: {explanation}
Student Answer: {userAnswer}

Provide the score (0-{maxScore}) and a short comment. 
Output JSON format: {{ ""Score"": 10, ""Comment"": ""..."", ""IsCorrect"": true }}
IMPORTANT: Return ONLY the raw JSON string. Do not use markdown code blocks or any other formatting.";

        try
        {
            var response = await _ollamaService.AskQuestion(prompt);
            var json = response.Trim();
            
            // Try to find JSON if there is extra text
            var jsonStart = json.IndexOf('{');
            var jsonEnd = json.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                json = json.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            var resultDto = JsonConvert.DeserializeObject<AiGradingDto>(json);
            if (resultDto != null)
            {
                var comment = resultDto.Comment;
                if (!string.IsNullOrWhiteSpace(explanation))
                {
                    comment = $"{explanation}\n\nAI Comment: {comment}";
                }
                
                return new GradingResult
                {
                    IsCorrect = resultDto.IsCorrect,
                    Score = resultDto.Score,
                    Comment = comment
                };
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

    private class AiGradingDto
    {
        public bool IsCorrect { get; set; }
        public int Score { get; set; }
        public string Comment { get; set; }
    }
}