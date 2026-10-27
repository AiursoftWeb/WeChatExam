using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Services;

public class ExtractService : IExtractService
{
    private readonly IOllamaService _ollamaService;
    private readonly WeChatExamDbContext _dbContext;
    private readonly ITagService _tagService;
    private readonly KnowledgeExerciseValidation _exerciseValidation;

    public ExtractService(
        IOllamaService ollamaService,
        WeChatExamDbContext dbContext,
        ITagService tagService,
        KnowledgeExerciseValidation exerciseValidation)
    {
        _ollamaService = ollamaService;
        _dbContext = dbContext;
        _tagService = tagService;
        _exerciseValidation = exerciseValidation;
    }

    public async Task<string> GenerateJsonAsync(string material, string systemPrompt, CancellationToken token = default)
    {
        var prompt = $"{systemPrompt}\n\nMaterial:\n{material}";
        return await _ollamaService.AskQuestion(prompt, token);
    }

    public async Task SaveAsync(List<ExtractedKnowledgePoint> data, Guid knowledgeCategoryId, Guid questionCategoryId, CancellationToken token = default)
    {
        // Knowledge placement and question membership are independent classifications.
        // Validate again here: a queued extraction can outlive the selected destination.
        if (!await _dbContext.KnowledgeCategories.AnyAsync(c => c.Id == knowledgeCategoryId, token))
        {
            throw new InvalidOperationException("The selected knowledge category no longer exists.");
        }
        if (!await _dbContext.QuestionCategories.AnyAsync(c => c.Id == questionCategoryId, token))
        {
            throw new InvalidOperationException("The selected period no longer exists.");
        }

        // Recheck at save time as well as before queuing: pricing can change while AI runs.
        if (data.Any(k => k.Questions.Count > 0))
        {
            var error = await _exerciseValidation.ValidateExtractionAsync(knowledgeCategoryId, questionCategoryId, token);
            if (error != null) throw new InvalidOperationException(error);
        }
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync<(List<ExtractedKnowledgePoint>, Guid, CancellationToken), int>(
            state: (data, knowledgeCategoryId, token),
            operation: async (_, state, _) =>
            {
                var (dataList, catId, cancellationToken) = state;
                
                using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var nextOrder = (await _dbContext.KnowledgeCategoryKnowledgePoints
                        .Where(association => association.KnowledgeCategoryId == catId)
                        .MaxAsync(association => (int?)association.OrderIndex, cancellationToken) ?? -1) + 1;

                    foreach (var kpDto in dataList)
                    {
                        // Create KnowledgePoint
                        var kp = new KnowledgePoint
                        {
                            Id = Guid.NewGuid(),
                            Title = kpDto.KnowledgeTitle,
                            Content = kpDto.KnowledgeContent,
                            CreationTime = DateTime.UtcNow
                        };
                        _dbContext.KnowledgePoints.Add(kp);

                        // Link KP to Category
                        _dbContext.KnowledgeCategoryKnowledgePoints.Add(new KnowledgeCategoryKnowledgePoint
                        {
                            KnowledgeCategoryId = catId,
                            KnowledgePointId = kp.Id,
                            OrderIndex = nextOrder++
                        });

                        // Process Questions
                        foreach (var qDto in kpDto.Questions)
                        {
                            var questionId = Guid.NewGuid();
                            var gradingStrategy = DetermineGradingStrategy(qDto.QuestionType);
                            
                            var question = new Question
                            {
                                Id = questionId,
                                Content = qDto.QuestionContent,
                                QuestionType = qDto.QuestionType,
                                GradingStrategy = gradingStrategy,
                                Metadata = JsonConvert.SerializeObject(qDto.Metadata),
                                StandardAnswer = qDto.StandardAnswer,
                                Explanation = qDto.Explanation,
                                QuestionCategoryId = questionCategoryId,
                                CreationTime = DateTime.UtcNow
                            };
                            _dbContext.Questions.Add(question);

                            // Link Question to KnowledgePoint
                            _dbContext.KnowledgePointQuestions.Add(new KnowledgePointQuestion
                            {
                                KnowledgePointId = kp.Id,
                                QuestionId = questionId
                            });

                            // Process Tags
                            foreach (var tagName in qDto.Tags)
                            {
                                var tag = await _tagService.AddTagAsync(tagName);
                                _dbContext.QuestionTags.Add(new QuestionTag
                                {
                                    QuestionId = questionId,
                                    TagId = tag.Id
                                });
                            }
                        }
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    
                    return 0;
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            },
            verifySucceeded: null,
            cancellationToken: token
        );
    }

    private GradingStrategy DetermineGradingStrategy(QuestionType type)
    {
        return type switch
        {
            QuestionType.Choice => GradingStrategy.ExactMatch,
            QuestionType.Bool => GradingStrategy.ExactMatch,
            QuestionType.Blank => GradingStrategy.FuzzyMatch,
            QuestionType.ShortAnswer => GradingStrategy.AiEval,
            QuestionType.Essay => GradingStrategy.AiEval,
            QuestionType.NounExplanation => GradingStrategy.AiEval,
            _ => GradingStrategy.ExactMatch
        };
    }
}
