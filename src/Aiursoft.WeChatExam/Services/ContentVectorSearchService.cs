using Aiursoft.Scanner.Abstractions;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ContentSearchViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.WeChatExam.Services;

public class ContentVectorSearchService(
    WeChatExamDbContext db,
    ContentEmbeddingCache cache,
    IGlobalSettingsService settings,
    EmbeddingService embeddings,
    IMemoryCache queryCache,
    ILogger<ContentVectorSearchService> logger) : IScopedDependency
{
    public const int MaxResults = 50;

    public async Task<(List<ContentSearchItem> Results, bool UsedAi)> SearchAsync(
        string query, string kind, Guid? knowledgeCategoryId, QuestionType? questionType = null,
        bool useAi = true, Guid? excludePaperId = null, Guid? questionCategoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return ([], false);
        // Question periods and knowledge directories are independent scopes, including in mixed results.
        var questions = db.Questions.AsNoTracking();
        if (questionCategoryId.HasValue)
            questions = questions.Where(q => q.QuestionCategoryId == questionCategoryId.Value);
        if (questionType.HasValue) questions = questions.Where(q => q.QuestionType == questionType);
        if (excludePaperId.HasValue)
            questions = questions.Where(q => !db.PaperQuestions.Any(pq =>
                pq.PaperId == excludePaperId.Value && pq.QuestionId == q.Id));
        var points = db.KnowledgePoints.AsNoTracking().AsQueryable();
        if (knowledgeCategoryId.HasValue)
            points = points.Where(k => k.KnowledgeCategoryKnowledgePoints.Any(c => c.KnowledgeCategoryId == knowledgeCategoryId.Value));
        var terms = query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(8).ToArray();
        var lowerQuery = query.ToLowerInvariant();
        var results = new List<ContentSearchItem>();
        if (kind != "knowledge")
        {
            var keywords = questions;
            foreach (var term in terms)
                keywords = keywords.Where(q => q.Content.ToLower().Contains(term) || q.Metadata.ToLower().Contains(term) ||
                    q.StandardAnswer.ToLower().Contains(term) || q.Explanation.ToLower().Contains(term));
            results.AddRange(await ProjectQuestions(keywords
                .OrderByDescending(q => q.Content.ToLower() == lowerQuery)
                .ThenByDescending(q => q.Content.ToLower().Contains(lowerQuery)).ThenBy(q => q.Id)
                .Take(MaxResults)).ToListAsync(cancellationToken));
        }
        if (kind != "questions")
        {
            var keywords = points;
            foreach (var term in terms)
                keywords = keywords.Where(k => k.Title.ToLower().Contains(term) || k.Content.ToLower().Contains(term));
            results.AddRange(await ProjectKnowledgePoints(keywords
                .OrderByDescending(k => k.Title.ToLower() == lowerQuery)
                .ThenByDescending(k => k.Title.ToLower().Contains(lowerQuery)).ThenBy(k => k.Id)
                .Take(MaxResults)).ToListAsync(cancellationToken));
        }
        foreach (var item in results)
            item.Score = item.Title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 1000 :
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 100 : 10;

        var usedAi = false;
        if (useAi && await settings.IsAiSearchEnabledAsync())
        {
            if (!cache.Snapshot().Loaded) await cache.LoadAsync(db, cancellationToken);
            var snapshot = cache.Snapshot();
            if ((kind != "knowledge" && snapshot.Questions.Count > 0) ||
                (kind != "questions" && snapshot.KnowledgePoints.Count > 0))
            {
                var vector = await TryEmbedQueryAsync(query, cancellationToken);
                if (vector != null)
                {
                    usedAi = true;
                    if (kind != "knowledge")
                        results.AddRange(await SimilarQuestionsAsync(questions, vector, MaxResults, cancellationToken));
                    if (kind != "questions")
                        results.AddRange(await SimilarKnowledgePointsAsync(points, vector, MaxResults, cancellationToken));
                }
            }
        }
        return (results.GroupBy(r => (r.IsKnowledgePoint, r.Id)).Select(g => g.MaxBy(r => r.Score)!)
            .OrderByDescending(r => r.Score).ThenBy(r => r.Title).ThenBy(r => r.Id).Take(MaxResults).ToList(), usedAi);
    }

    public async Task<(bool Available, List<ContentSearchItem> Results)> RecommendAsync(
        RecommendationRequest request, bool isKnowledgePoint, CancellationToken cancellationToken = default)
    {
        if (!await settings.IsAiSearchEnabledAsync()) return (false, []);
        if (request.Text != null && string.IsNullOrWhiteSpace(request.Text)) return (true, []);
        var vector = await RecommendationVectorAsync(request, isKnowledgePoint, cancellationToken);
        if (vector == null) return (false, []);
        var questions = db.Questions.AsNoTracking().Where(q => !request.ExcludeIds.Contains(q.Id));
        // Knowledge points can relate to questions across periods; their directory is not a question filter.
        if (!isKnowledgePoint && request.QuestionCategoryId.HasValue)
            questions = questions.Where(q => q.QuestionCategoryId == request.QuestionCategoryId);
        if (isKnowledgePoint)
            questions = questions.Where(q => !q.KnowledgePointQuestions.Any(k => k.KnowledgePointId == request.Id));
        else
            questions = questions.Where(q => q.Id != request.Id);
        return (true, await SimilarQuestionsAsync(questions, vector, 5, cancellationToken));
    }

    public async Task<(bool Available, List<ContentSearchItem> Results)> SimilarKnowledgePointsAsync(
        RecommendationRequest request, CancellationToken cancellationToken = default)
    {
        if (!await settings.IsAiSearchEnabledAsync()) return (false, []);
        if (request.Text != null && string.IsNullOrWhiteSpace(request.Text)) return (true, []);
        var vector = await RecommendationVectorAsync(request, true, cancellationToken);
        if (vector == null) return (false, []);
        var points = db.KnowledgePoints.AsNoTracking().Where(k => k.Id != request.Id && !request.ExcludeIds.Contains(k.Id));
        return (true, await SimilarKnowledgePointsAsync(points, vector, 5, cancellationToken));
    }

    private async Task<float[]?> RecommendationVectorAsync(RecommendationRequest request, bool isKnowledgePoint,
        CancellationToken cancellationToken)
    {
        if (request.Refresh || !cache.Snapshot().Loaded) await cache.LoadAsync(db, cancellationToken);
        if (request.Text != null) return await TryEmbedQueryAsync(request.Text, cancellationToken);
        byte[]? stored;
        string savedText;
        if (isKnowledgePoint)
        {
            var point = await db.KnowledgePoints.AsNoTracking().SingleAsync(k => k.Id == request.Id, cancellationToken);
            stored = point.LastEmbeddedAt >= point.LastUpdatedAt ? point.Embedding : null;
            savedText = $"{point.Title}\n{point.Content}";
        }
        else
        {
            var question = await db.Questions.AsNoTracking().SingleAsync(q => q.Id == request.Id, cancellationToken);
            stored = question.LastEmbeddedAt >= question.LastUpdatedAt ? question.Embedding : null;
            savedText = $"{question.QuestionType}\n{question.Content}\n{question.Metadata}\n{question.StandardAnswer}\n{question.Explanation}";
        }
        return EmbeddingHelper.Deserialize(stored) ?? await TryEmbedQueryAsync(savedText, cancellationToken);
    }

    private async Task<float[]?> TryEmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        var model = await settings.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var endpoint = await settings.GetEmbeddingEndpointAsync();
        var key = (typeof(ContentVectorSearchService), endpoint, model, text);
        if (queryCache.TryGetValue(key, out float[]? cached)) return cached;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var vector = await embeddings.GenerateAsync(text, timeout.Token);
            queryCache.Set(key, vector, TimeSpan.FromMinutes(15));
            return vector;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Content query embedding unavailable; retaining keyword search.");
            return null;
        }
    }

    private async Task<List<ContentSearchItem>> SimilarQuestionsAsync(IQueryable<Question> questions,
        float[] vector, int take, CancellationToken cancellationToken)
    {
        var snapshot = cache.Snapshot().Questions;
        var ids = snapshot.Keys.ToList();
        var current = await questions.Where(q => ids.Contains(q.Id) && q.Embedding != null && q.LastEmbeddedAt >= q.LastUpdatedAt)
            .Select(q => new { q.Id, q.LastUpdatedAt }).ToDictionaryAsync(q => q.Id, q => q.LastUpdatedAt, cancellationToken);
        var scores = Rank(snapshot, current, vector, take);
        var topIds = scores.Keys.ToList();
        var results = await ProjectQuestions(questions.Where(q => topIds.Contains(q.Id))).ToListAsync(cancellationToken);
        foreach (var result in results) result.Score = scores[result.Id];
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Id).ToList();
    }

    private async Task<List<ContentSearchItem>> SimilarKnowledgePointsAsync(IQueryable<KnowledgePoint> points,
        float[] vector, int take, CancellationToken cancellationToken)
    {
        var snapshot = cache.Snapshot().KnowledgePoints;
        var ids = snapshot.Keys.ToList();
        var current = await points.Where(k => ids.Contains(k.Id) && k.Embedding != null && k.LastEmbeddedAt >= k.LastUpdatedAt)
            .Select(k => new { k.Id, k.LastUpdatedAt }).ToDictionaryAsync(k => k.Id, k => k.LastUpdatedAt, cancellationToken);
        var scores = Rank(snapshot, current, vector, take);
        var topIds = scores.Keys.ToList();
        var results = await ProjectKnowledgePoints(points.Where(k => topIds.Contains(k.Id))).ToListAsync(cancellationToken);
        foreach (var result in results) result.Score = scores[result.Id];
        return results.OrderByDescending(r => r.Score).ThenBy(r => r.Id).ToList();
    }

    private static Dictionary<Guid, float> Rank(IReadOnlyDictionary<Guid, ContentEmbeddingCache.Entry> snapshot,
        Dictionary<Guid, DateTime> current, float[] vector, int take) => current
        .Where(kv => snapshot[kv.Key].UpdatedAt == kv.Value && snapshot[kv.Key].Vector.Length == vector.Length)
        .Select(kv => (kv.Key, Score: EmbeddingHelper.CosineSimilarity(vector, snapshot[kv.Key].Vector)))
        .Where(kv => kv.Score > 0).OrderByDescending(kv => kv.Score).ThenBy(kv => kv.Key)
        .Take(take).ToDictionary(kv => kv.Key, kv => kv.Score);

    private static IQueryable<ContentSearchItem> ProjectQuestions(IQueryable<Question> query) => query.Select(q => new ContentSearchItem
    {
        Id = q.Id, Title = q.Content, Preview = q.Explanation, Answer = q.StandardAnswer,
        QuestionType = q.QuestionType,
        Category = q.QuestionCategory!.TotalCategory!.Title + " · " + q.QuestionCategory.Title
    });

    private static IQueryable<ContentSearchItem> ProjectKnowledgePoints(IQueryable<KnowledgePoint> query) => query.Select(k => new ContentSearchItem
    {
        Id = k.Id, IsKnowledgePoint = true, Title = k.Title, Preview = k.Content
    });
}
