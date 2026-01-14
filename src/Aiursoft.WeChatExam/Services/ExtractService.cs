using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Services;

public class ExtractService : IExtractService
{
    private readonly IOllamaService _ollamaService;
    private readonly TemplateDbContext _dbContext;
    private readonly ITagService _tagService;

    public ExtractService(
        IOllamaService ollamaService,
        TemplateDbContext dbContext,
        ITagService tagService)
    {
        _ollamaService = ollamaService;
        _dbContext = dbContext;
        _tagService = tagService;
    }

    public async Task<string> GenerateJsonAsync(string material, string systemPrompt, CancellationToken token = default)
    {
        var prompt = $"{systemPrompt}\n\nMaterial:\n{material}";
        return await _ollamaService.AskQuestion(prompt, token);
    }

    public async Task SaveAsync(List<ExtractedKnowledgePoint> data, Guid categoryId, CancellationToken token = default)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync<(List<ExtractedKnowledgePoint>, Guid, CancellationToken), int>(
            state: (data, categoryId, token),
            operation: async (_, state, _) =>
            {
                var (dataList, catId, cancellationToken) = state;
                
                using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    foreach (var kpDto in dataList)
                    {
                        // Create KnowledgePoint
                        var kp = new KnowledgePoint
                        {
                            Id = Guid.NewGuid(),
                            Title = kpDto.KnowledgeTitle,
                            Content = kpDto.KnowledgeContent,
                            ParentId = null,
                            CreationTime = DateTime.UtcNow
                        };
                        _dbContext.KnowledgePoints.Add(kp);

                        // Link KP to Category
                        _dbContext.CategoryKnowledgePoints.Add(new CategoryKnowledgePoint
                        {
                            CategoryId = catId,
                            KnowledgePointId = kp.Id
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
                                CategoryId = catId,
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
            _ => GradingStrategy.ExactMatch
        };
    }
}
