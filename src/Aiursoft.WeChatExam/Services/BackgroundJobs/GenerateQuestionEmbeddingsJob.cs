using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services.BackgroundJobs;

public class GenerateQuestionEmbeddingsJob(
    WeChatExamDbContext db,
    IGlobalSettingsService settingsService,
    EmbeddingService embeddingService,
    RetryEngine retryEngine,
    ILogger<GenerateQuestionEmbeddingsJob> logger) : IBackgroundJob
{
    private const int MaxDocumentsPerRun = 50;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public string Name => "Generate Question Embeddings";
    public string Description => "Generates embedding vectors for new or changed question content using the configured Ollama model.";

    public async Task ExecuteAsync()
    {
        if (!await RunLock.WaitAsync(0))
        {
            logger.LogInformation("GenerateQuestionEmbeddingsJob: previous run is still active. Skipping.");
            return;
        }

        try
        {
            if (!await settingsService.IsAiSearchEnabledAsync())
            {
                logger.LogInformation("GenerateQuestionEmbeddingsJob: Embedding generation is disabled or not configured. Skipping.");
                return;
            }

            var pending = await db.Questions
                .Where(item => item.Embedding == null || item.LastEmbeddedAt < item.LastUpdatedAt)
                .OrderBy(item => item.LastEmbeddedAt)
                .ThenBy(item => item.LastUpdatedAt)
                .ThenBy(item => item.Id)
                .Take(MaxDocumentsPerRun)
                .ToListAsync();
            var succeeded = 0;
            foreach (var item in pending)
            {
                try
                {
                    var sourceUpdatedAt = item.LastUpdatedAt;
                    var text = $"{item.QuestionType}\n{item.Content}\n{item.Metadata}\n{item.StandardAnswer}\n{item.Explanation}";
                    var vector = await retryEngine.RunWithRetry(async _ => await embeddingService.GenerateAsync(text));
                    var serialized = EmbeddingHelper.Serialize(vector);
                    if (await TrySaveEmbeddingIfUnchangedAsync(item, sourceUpdatedAt, serialized))
                        succeeded++;
                    else
                        logger.LogInformation("GenerateQuestionEmbeddingsJob: {Id} changed or was deleted during generation. Skipping stale result.", item.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "GenerateQuestionEmbeddingsJob: Failed to generate embedding for {Id}.", item.Id);
                }
            }

            logger.LogInformation("GenerateQuestionEmbeddingsJob: {Succeeded}/{Attempted} documents processed.", succeeded, pending.Count);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<bool> TrySaveEmbeddingIfUnchangedAsync(Question item, DateTime sourceUpdatedAt, byte[] embedding)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.Questions
                .Where(current => current.Id == item.Id && current.LastUpdatedAt == sourceUpdatedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(current => current.Embedding, embedding)
                    .SetProperty(current => current.LastEmbeddedAt, sourceUpdatedAt));
            return updated == 1;
        }

        await db.Entry(item).ReloadAsync();
        if (db.Entry(item).State == EntityState.Detached || item.LastUpdatedAt != sourceUpdatedAt)
            return false;

        item.Embedding = embedding;
        item.LastEmbeddedAt = sourceUpdatedAt;
        await db.SaveChangesAsync();
        return true;
    }
}
