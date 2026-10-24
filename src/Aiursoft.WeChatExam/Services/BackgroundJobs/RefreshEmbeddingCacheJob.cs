using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services.BackgroundJobs;

public class RefreshEmbeddingCacheJob(
    WeChatExamDbContext db,
    ContentEmbeddingCache cache,
    IGlobalSettingsService settingsService) : IBackgroundJob
{
    public string Name => "Refresh Embedding Cache";
    public string Description => "Reloads question and knowledge point embedding vectors from the database.";

    public async Task ExecuteAsync()
    {
        if (await settingsService.IsAiSearchEnabledAsync())
            await cache.LoadAsync(db);
    }
}
