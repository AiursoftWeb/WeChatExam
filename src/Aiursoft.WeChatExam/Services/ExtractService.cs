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
        using var transaction = await _dbContext.Database.BeginTransactionAsync(token);

        try
        {
            foreach (var kpDto in data)
            {
                // Create KnowledgePoint
                var kp = new KnowledgePoint
                {
                    Id = Guid.NewGuid(),
                    Title = kpDto.KnowledgeTitle,
                    Content = kpDto.KnowledgeContent,
                    ParentId = null, // Top level for this extraction? Or not supported in UI yet.
                    CreationTime = DateTime.UtcNow
                };

                _dbContext.KnowledgePoints.Add(kp);
                
                // Link KP to Category
                _dbContext.CategoryKnowledgePoints.Add(new CategoryKnowledgePoint
                {
                    CategoryId = categoryId,
                    KnowledgePointId = kp.Id
                });

                // Process Questions
                foreach (var qDto in kpDto.Questions)
                {
                    var questionId = Guid.NewGuid();
                    var strategy = DetermineGradingStrategy(qDto.QuestionType);

                    var question = new Question
                    {
                        Id = questionId,
                        Content = qDto.QuestionContent,
                        QuestionType = qDto.QuestionType,
                        GradingStrategy = strategy,
                        Metadata = JsonConvert.SerializeObject(qDto.Metadata),
                        StandardAnswer = qDto.StandardAnswer,
                        Explanation = qDto.Explanation,
                        CategoryId = categoryId,
                        CreationTime = DateTime.UtcNow
                    };

                    _dbContext.Questions.Add(question);

                    // Link Question to KnowledgePoint
                    _dbContext.KnowledgePointQuestions.Add(new KnowledgePointQuestion
                    {
                        KnowledgePointId = kp.Id,
                        QuestionId = questionId
                    });

                    // Process Tags (using TagService for deduplication)
                    // We need to save changes so far? No, AddTagAsync saves internally.
                    foreach (var tagName in qDto.Tags)
                    {
                         // This will save the tag if new. 
                         // It participates in the current transaction.
                        var tag = await _tagService.AddTagAsync(tagName);
                        
                        _dbContext.QuestionTags.Add(new QuestionTag
                        {
                            QuestionId = questionId,
                            TagId = tag.Id
                        });
                    }
                }
            }

            await _dbContext.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
        catch
        {
            await transaction.RollbackAsync(token);
            throw;
        }
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
