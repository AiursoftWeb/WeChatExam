using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Service for batch AI classification of questions using OllamaService.
/// Enqueues classification jobs into BackgroundJobQueue with concurrency and retry support.
/// </summary>
public class AiClassificationService(
    BackgroundJobQueue backgroundJobQueue,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<AiClassificationService> logger)
{
    /// <summary>
    /// Default number of concurrent classification queues.
    /// </summary>
    private const int DefaultConcurrency = 3;

    /// <summary>
    /// Maximum number of retry attempts for each classification job.
    /// </summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds.
    /// </summary>
    private const int RetryDelayMs = 2000;

    /// <summary>
    /// Queue name prefix for classification jobs.
    /// </summary>
    private const string QueuePrefix = "AiClassify";

    /// <summary>
    /// Enqueue classification jobs for the given questions using the specified candidate categories.
    /// Returns the number of jobs enqueued (skips questions that already have pending/processing jobs).
    /// </summary>
    public async Task<int> EnqueueClassificationJobs(Guid[] questionIds, Guid[] categoryIds)
    {
        if (questionIds.Length == 0)
            throw new ArgumentException("No questions selected for classification.", nameof(questionIds));
        if (categoryIds.Length == 0)
            throw new ArgumentException("No categories selected for classification.", nameof(categoryIds));

        // Load category id->title mapping from DB
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();

        var categories = await context.Categories
            .Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Title })
            .ToListAsync();

        if (categories.Count == 0)
            throw new ArgumentException("None of the specified categories exist.", nameof(categoryIds));

        var categoryMap = categories.ToDictionary(c => c.Title, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var categoryTitles = categories.Select(c => c.Title).ToArray();

        // Check for duplicate submissions: skip questions that already have pending/processing jobs
        var pendingJobs = backgroundJobQueue.GetPendingJobs()
            .Concat(backgroundJobQueue.GetProcessingJobs())
            .Where(j => j.QueueName.StartsWith(QueuePrefix))
            .Select(j => j.JobName)
            .ToHashSet();

        var enqueuedCount = 0;

        for (var i = 0; i < questionIds.Length; i++)
        {
            var questionId = questionIds[i];
            var jobName = $"AI Classify: {questionId}";

            // Skip if already pending/processing
            if (pendingJobs.Contains(jobName))
            {
                logger.LogWarning("Skipping question {QuestionId}: already has a pending/processing classification job", questionId);
                continue;
            }

            // Round-robin across concurrent queues
            var queueName = $"{QueuePrefix}-{i % DefaultConcurrency}";

            // Capture variables for the closure
            var capturedQuestionId = questionId;
            var capturedCategoryTitles = categoryTitles;
            var capturedCategoryMap = categoryMap;

            backgroundJobQueue.QueueWithDependency<IOllamaService>(
                queueName: queueName,
                jobName: jobName,
                job: async (ollamaService) =>
                {
                    return await ClassifySingleQuestion(
                        capturedQuestionId,
                        capturedCategoryTitles,
                        capturedCategoryMap,
                        ollamaService);
                });

            enqueuedCount++;
        }

        logger.LogInformation(
            "Enqueued {Count} AI classification jobs for {Total} questions across {Queues} queues",
            enqueuedCount, questionIds.Length, DefaultConcurrency);

        return enqueuedCount;
    }

    /// <summary>
    /// Classify a single question using OllamaService with retry logic.
    /// This runs inside a background job and creates its own DB scope.
    /// </summary>
    private async Task<string?> ClassifySingleQuestion(
        Guid questionId,
        string[] categoryTitles,
        Dictionary<string, Guid> categoryMap,
        IOllamaService ollamaService)
    {
        // Create a new scope for DB access since this runs in a background job
        using var scope = serviceScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var jobLogger = scope.ServiceProvider.GetRequiredService<ILogger<AiClassificationService>>();

        // Load the question
        var question = await context.Questions.FindAsync(questionId);
        if (question == null)
        {
            throw new InvalidOperationException($"Question {questionId} not found.");
        }

        if (string.IsNullOrWhiteSpace(question.Content))
        {
            throw new InvalidOperationException($"Question {questionId} has no content, cannot classify.");
        }

        // Build the prompt
        var categoryListStr = string.Join(", ", categoryTitles.Select(t => $"\"{t}\""));
        var prompt = $$"""
            You are a question classifier. Your task is to classify the following question into exactly ONE of the given categories.

            Available categories: [{{categoryListStr}}]

            Question: {{question.Content}}

            Rules:
            1. You MUST choose exactly one category from the list above.
            2. You MUST NOT output any category that is not in the list.
            3. You MUST output ONLY valid JSON in the exact format below, with no extra text.

            Output format:
            {"category":"<selected category title>"}
            """;

        // Retry logic
        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await ollamaService.AskQuestion(prompt);

                // Parse the JSON response
                var categoryTitle = ParseCategoryFromResponse(response, categoryTitles);

                if (!categoryMap.TryGetValue(categoryTitle, out var categoryId))
                {
                    throw new InvalidOperationException(
                        $"AI returned category '{categoryTitle}' which is not in the allowed list.");
                }

                // Update the question's category
                question.CategoryId = categoryId;
                context.Questions.Update(question);
                await context.SaveChangesAsync();

                var contentPreview = question.Content.Length > 50
                    ? question.Content[..50] + "..."
                    : question.Content;

                jobLogger.LogInformation(
                    "Successfully classified question {QuestionId} → '{Category}'",
                    questionId, categoryTitle);

                return $"Classified '{contentPreview}' → '{categoryTitle}'";
            }
            catch (Exception ex)
            {
                lastException = ex;
                jobLogger.LogWarning(
                    ex,
                    "AI classification attempt {Attempt}/{MaxRetries} failed for question {QuestionId}",
                    attempt, MaxRetries, questionId);

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelayMs);
                }
            }
        }

        // All retries exhausted
        throw new InvalidOperationException(
            $"AI classification failed for question {questionId} after {MaxRetries} attempts: {lastException?.Message}",
            lastException);
    }

    /// <summary>
    /// Parse the category title from the AI JSON response.
    /// Validates that it is one of the allowed categories.
    /// </summary>
    private static string ParseCategoryFromResponse(string response, string[] allowedCategories)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException("AI returned an empty response.");
        }

        // Try to extract JSON from the response (AI might include extra text)
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart < 0 || jsonEnd < 0 || jsonEnd <= jsonStart)
        {
            throw new InvalidOperationException($"AI response does not contain valid JSON: {response}");
        }

        var jsonStr = response[jsonStart..(jsonEnd + 1)];

        try
        {
            var result = JsonConvert.DeserializeAnonymousType(jsonStr, new { category = "" });

            if (string.IsNullOrWhiteSpace(result?.category))
            {
                throw new InvalidOperationException($"AI response JSON does not contain a 'category' field: {jsonStr}");
            }

            // Validate the category is in the allowed list (case-insensitive)
            var matchedCategory = allowedCategories
                .FirstOrDefault(c => string.Equals(c, result.category, StringComparison.OrdinalIgnoreCase));

            if (matchedCategory == null)
            {
                throw new InvalidOperationException(
                    $"AI returned category '{result.category}' which is not in the allowed list: [{string.Join(", ", allowedCategories)}]");
            }

            return matchedCategory;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse AI response as JSON: {jsonStr}", ex);
        }
    }
}
