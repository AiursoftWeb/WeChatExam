using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class OptimizationService(
    WeChatExamDbContext dbContext,
    IOllamaService ollamaService,
    ILogger<OptimizationService> logger) : IOptimizationService
{
    public async Task OptimizeNounExplanations(CancellationToken token = default)
    {
        var questions = await dbContext.Questions
            .Where(q => q.QuestionType == QuestionType.NounExplanation)
            .ToListAsync(token);

        logger.LogInformation("Starting to optimize {Count} noun explanation questions.", questions.Count);

        foreach (var question in questions)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var prompt = $"请你扮演一位考研音乐名师，在编写教材答案。现在需要名词解释{question.Content}。以音乐方面为主。限制在100字以内。";
                var newAnswer = await ollamaService.AskQuestion(prompt, token);
                
                if (!string.IsNullOrWhiteSpace(newAnswer))
                {
                    question.StandardAnswer = newAnswer.Trim();
                    await dbContext.SaveChangesAsync(token);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to optimize noun explanation for question {Id}.", question.Id);
            }
        }
        
        logger.LogInformation("Finished optimizing noun explanation questions.");
    }

    public async Task RegenerateExplanations(CancellationToken token = default)
    {
        var questions = await dbContext.Questions.ToListAsync(token);

        logger.LogInformation("Starting to regenerate explanations for {Count} questions.", questions.Count);

        foreach (var question in questions)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var prompt = $"{question.Content} + {question.StandardAnswer}\n\n上面这句话太笼统了，你给我扩展一下200字以内的材料，详细解释一下这个题目的背景和逻辑。";
                var newExplanation = await ollamaService.AskQuestion(prompt, token);
                
                if (!string.IsNullOrWhiteSpace(newExplanation))
                {
                    question.Explanation = newExplanation.Trim();
                    await dbContext.SaveChangesAsync(token);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to regenerate explanation for question {Id}.", question.Id);
            }
        }
        
        logger.LogInformation("Finished regenerating explanations.");
    }
}
