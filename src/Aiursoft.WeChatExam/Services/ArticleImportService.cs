using System.Text.Json;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.DTOs;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for importing articles and extracting questions using AI
/// </summary>
public class ArticleImportService : IArticleImportService
{
    private readonly IOllamaService _ollamaService;
    private readonly BackgroundJobQueue _backgroundJobQueue;
    private readonly ILogger<ArticleImportService> _logger;
    private readonly TemplateDbContext _dbContext;

    public ArticleImportService(
        IOllamaService ollamaService,
        BackgroundJobQueue backgroundJobQueue,
        ILogger<ArticleImportService> logger,
        TemplateDbContext dbContext)
    {
        _ollamaService = ollamaService;
        _backgroundJobQueue = backgroundJobQueue;
        _logger = logger;
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<ArticleImportResultDto> ImportArticleAsync(
        ArticleImportDto importDto, 
        string authorId, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting article import for user {AuthorId}", authorId);

            // Create the article
            var article = new Article
            {
                Title = importDto.Title,
                Content = importDto.Content,
                AuthorId = authorId
            };

            _dbContext.Articles.Add(article);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Extract questions from content
            var extractedQuestions = await ExtractQuestionsFromContentAsync(importDto.Content, cancellationToken);

            // Create questions in database
            var questionIds = new List<Guid>();
            foreach (var questionDto in extractedQuestions)
            {
                var question = new Question
                {
                    Content = questionDto.Content,
                    QuestionType = questionDto.QuestionType,
                    GradingStrategy = questionDto.GradingStrategy,
                    Metadata = questionDto.Metadata,
                    StandardAnswer = questionDto.StandardAnswer,
                    Explanation = questionDto.Explanation,
                    CategoryId = importDto.CategoryId
                };

                _dbContext.Questions.Add(question);
                await _dbContext.SaveChangesAsync(cancellationToken);

                questionIds.Add(question.Id);

                // Add tags to question
                await AddTagsToQuestionAsync(question.Id, questionDto.Tags, cancellationToken);
            }

            // Add user-specified tags to all questions
            if (importDto.Tags.Any())
            {
                foreach (var questionId in questionIds)
                {
                    await AddTagsToQuestionAsync(questionId, importDto.Tags, cancellationToken);
                }
            }

            _logger.LogInformation("Successfully imported article {ArticleId} with {QuestionCount} questions", 
                article.Id, questionIds.Count);

            return new ArticleImportResultDto
            {
                ArticleId = article.Id,
                QuestionsExtracted = questionIds.Count,
                QuestionIds = questionIds,
                Status = ImportStatus.Completed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import article for user {AuthorId}", authorId);
            return new ArticleImportResultDto
            {
                Status = ImportStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async Task<Guid> ImportArticleAsync(ArticleImportDto importDto, string authorId)
    {
        var jobId = _backgroundJobQueue.QueueWithDependency<IArticleImportService>(
            queueName: "ArticleImportQueue",
            jobName: $"Import article: {importDto.Title}",
            job: async (service) =>
            {
                await service.ImportArticleAsync(importDto, authorId);
            });

        _logger.LogInformation("Queued article import job {JobId} for user {AuthorId}", jobId, authorId);
        return jobId;
    }

    /// <inheritdoc/>
    public async Task<List<ExtractedQuestionDto>> ExtractQuestionsFromContentAsync(
        string articleContent, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $$"""
                Please analyze the following article content and extract meaningful questions from it. 
                Return the result as a JSON array of question objects.

                Article content:
                {{articleContent}}

                For each question, provide:
                - content: The question text
                - questionType: One of "Choice", "Blank", "Bool", "ShortAnswer", "Essay"
                - gradingStrategy: One of "ExactMatch", "FuzzyMatch", "AiEval"
                - metadata: JSON string with additional data (like choices for multiple choice)
                - standardAnswer: The correct answer
                - explanation: Brief explanation of the answer
                - tags: Array of relevant tags for this question

                Return only valid JSON array. Example format:
                [
                    {
                        "content": "What is the capital of France?",
                        "questionType": "Choice",
                        "gradingStrategy": "ExactMatch",
                        "metadata": "{\"choices\":[\"Paris\",\"London\",\"Berlin\",\"Madrid\"]}",
                        "standardAnswer": "Paris",
                        "explanation": "Paris is the capital and largest city of France.",
                        "tags": ["geography", "europe", "france"]
                    }
                ]
                """;

            var response = await _ollamaService.AskQuestion(prompt, cancellationToken);
            
            // Try to parse the JSON response
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var questions = JsonSerializer.Deserialize<List<ExtractedQuestionDto>>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return questions ?? new List<ExtractedQuestionDto>();
            }

            _logger.LogWarning("Failed to parse JSON response from AI for question extraction");
            return new List<ExtractedQuestionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting questions from content");
            return new List<ExtractedQuestionDto>();
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> ExtractTagsFromContentAsync(
        string articleContent, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $$"""
                Please analyze the following article content and extract relevant tags/keywords.
                Return the result as a JSON array of strings.

                Article content:
                {{articleContent}}

                Extract 5-10 most relevant tags that describe the content.
                Tags should be single words or short phrases, lowercase.
                Examples: ["technology", "artificial-intelligence", "programming", "science"]

                Return only valid JSON array.
                """;

            var response = await _ollamaService.AskQuestion(prompt, cancellationToken);
            
            // Try to parse the JSON response
            var jsonStart = response.IndexOf('[');
            var jsonEnd = response.LastIndexOf(']');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonString = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var tags = JsonSerializer.Deserialize<List<string>>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return tags ?? new List<string>();
            }

            _logger.LogWarning("Failed to parse JSON response from AI for tag extraction");
            return new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting tags from content");
            return new List<string>();
        }
    }

    private async Task AddTagsToQuestionAsync(Guid questionId, List<string> tags, CancellationToken cancellationToken)
    {
        if (!tags.Any()) return;

        foreach (var tagName in tags)
        {
            var normalizedName = tagName.ToLowerInvariant().Trim();
            
            // Find or create tag
            var tag = await _dbContext.Tags.FirstOrDefaultAsync(t => t.NormalizedName == normalizedName, cancellationToken);
            if (tag == null)
            {
                tag = new Tag 
                { 
                    DisplayName = tagName,
                    NormalizedName = normalizedName
                };
                _dbContext.Tags.Add(tag);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            // Create question-tag relationship
            var questionTag = new QuestionTag
            {
                QuestionId = questionId,
                TagId = tag.Id
            };

            _dbContext.QuestionTags.Add(questionTag);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}