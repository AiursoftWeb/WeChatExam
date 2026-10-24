using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Util;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Immutable snapshots of the two content indexes, following the neighbouring embedding caches.</summary>
public class ContentEmbeddingCache(ILogger<ContentEmbeddingCache> logger)
{
    private const int MaxEntries = 10000;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly Lock _lock = new();
    private IReadOnlyDictionary<Guid, Entry> _questions = new Dictionary<Guid, Entry>();
    private IReadOnlyDictionary<Guid, Entry> _knowledgePoints = new Dictionary<Guid, Entry>();
    private bool _loaded;

    public sealed class Entry(DateTime updatedAt, float[] vector)
    {
        public DateTime UpdatedAt { get; } = updatedAt;
        public float[] Vector { get; } = vector;
    }

    public (bool Loaded, IReadOnlyDictionary<Guid, Entry> Questions, IReadOnlyDictionary<Guid, Entry> KnowledgePoints) Snapshot()
    {
        lock (_lock) return (_loaded, _questions, _knowledgePoints);
    }

    public async Task LoadAsync(WeChatExamDbContext db, CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            var questions = await db.Questions.AsNoTracking()
                .Where(q => q.Embedding != null && q.LastEmbeddedAt >= q.LastUpdatedAt)
                .OrderBy(q => q.Id).Take(MaxEntries + 1)
                .Select(q => new { q.Id, q.LastUpdatedAt, q.Embedding }).ToListAsync(cancellationToken);
            var points = await db.KnowledgePoints.AsNoTracking()
                .Where(k => k.Embedding != null && k.LastEmbeddedAt >= k.LastUpdatedAt)
                .OrderBy(k => k.Id).Take(MaxEntries + 1)
                .Select(k => new { k.Id, k.LastUpdatedAt, k.Embedding }).ToListAsync(cancellationToken);
            if (questions.Count > MaxEntries || points.Count > MaxEntries)
                logger.LogWarning("Content embedding cache reached its {Limit} entries per content type.", MaxEntries);

            var questionCache = new Dictionary<Guid, Entry>();
            var pointCache = new Dictionary<Guid, Entry>();
            foreach (var item in questions.Take(MaxEntries))
            {
                var vector = EmbeddingHelper.Deserialize(item.Embedding);
                if (vector is { Length: 1024 } && vector.All(float.IsFinite))
                    questionCache[item.Id] = new Entry(item.LastUpdatedAt, vector);
            }
            foreach (var item in points.Take(MaxEntries))
            {
                var vector = EmbeddingHelper.Deserialize(item.Embedding);
                if (vector is { Length: 1024 } && vector.All(float.IsFinite))
                    pointCache[item.Id] = new Entry(item.LastUpdatedAt, vector);
            }
            lock (_lock)
            {
                _questions = questionCache;
                _knowledgePoints = pointCache;
                _loaded = true;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }
}
