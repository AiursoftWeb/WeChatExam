using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services.BackgroundJobs;

public class GenerateKnowledgePointEmbeddingsJob(
    WeChatExamDbContext db,
    IGlobalSettingsService settingsService,
    EmbeddingService embeddingService,
    RetryEngine retryEngine,
    ILogger<GenerateKnowledgePointEmbeddingsJob> logger) : IBackgroundJob
{
    private const int MaxDocumentsPerRun = 50;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public string Name => "Generate Knowledge Point Embeddings";
    public string Description => "Generates embedding vectors for new or changed knowledge point content using the configured Ollama model.";

    public async Task ExecuteAsync()
    {
        if (!await RunLock.WaitAsync(0))
        {
            logger.LogInformation("GenerateKnowledgePointEmbeddingsJob: previous run is still active. Skipping.");
            return;
        }

        try
        {
            if (!await settingsService.IsAiSearchEnabledAsync())
            {
                logger.LogInformation("GenerateKnowledgePointEmbeddingsJob: Embedding generation is disabled or not configured. Skipping.");
                return;
            }

            var pending = await db.KnowledgePoints
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
                    var text = $"{item.Title}\n{item.Content}";
                    var vector = await retryEngine.RunWithRetry(async _ => await embeddingService.GenerateAsync(text));
                    var serialized = EmbeddingHelper.Serialize(vector);
                    if (await TrySaveEmbeddingIfUnchangedAsync(item, sourceUpdatedAt, serialized))
                        succeeded++;
                    else
                        logger.LogInformation("GenerateKnowledgePointEmbeddingsJob: {Id} changed or was deleted during generation. Skipping stale result.", item.Id);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "GenerateKnowledgePointEmbeddingsJob: Failed to generate embedding for {Id}.", item.Id);
                }
            }

            logger.LogInformation("GenerateKnowledgePointEmbeddingsJob: {Succeeded}/{Attempted} documents processed.", succeeded, pending.Count);
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task<bool> TrySaveEmbeddingIfUnchangedAsync(KnowledgePoint item, DateTime sourceUpdatedAt, byte[] embedding)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.KnowledgePoints
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
