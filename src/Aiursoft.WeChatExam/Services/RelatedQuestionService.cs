using Aiursoft.Scanner.Abstractions;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Finds a small, authorized practice set around an owned historical mistake.</summary>
public class RelatedQuestionService(WeChatExamDbContext db, ContentEmbeddingCache cache,
    IGlobalSettingsService settings, ContentVectorSearchService search) : IScopedDependency
{
    // Initial BGE candidate-quality heuristics, not mastery scores. Prefer returning fewer
    // questions to filling a set with weak matches or almost identical paraphrases.
    private const float MinimumSimilarity = 0.65f;
    private const float DuplicateSimilarity = 0.98f;
    private const int MaximumResults = 3;

    public async Task<RelatedQuestionsDto> FindAsync(MistakeRecord mistake, PaperAccessStatus access,
        CancellationToken cancellationToken = default)
    {
        if (!await settings.IsAiSearchEnabledAsync()) return new(false, []);
        // Old copies carry no course FK. If the original is gone, retain historical review
        // but do not guess a course from tags, the paper title or an institution.
        var source = await db.Questions.AsNoTracking().Include(q => q.QuestionCategory)
            .SingleOrDefaultAsync(q => q.Id == mistake.SourceQuestionId, cancellationToken);
        if (source?.QuestionCategory == null) return new(true, []);
        if (!cache.Snapshot().Loaded) await cache.LoadAsync(db, cancellationToken);
        var index = cache.Snapshot().Questions;
        if (index.Count == 0) return new(false, []);
        var indexedIds = index.Keys.ToList();

        // Apply independent practice rights BEFORE ranking or limiting. Owning a real paper
        // (or a historical copy) does not grant access to paid questions in the same course.
        var eligible = db.Questions.AsNoTracking().Where(QuestionAccess.PracticeFilter(access))
            .Where(q => q.Id != source.Id && indexedIds.Contains(q.Id) && q.Embedding != null &&
                q.LastEmbeddedAt >= q.LastUpdatedAt &&
                q.QuestionCategory!.TotalCategoryId == source.QuestionCategory.TotalCategoryId);
        var candidates = await eligible.Select(q => new
        {
            q.Id, q.Content, q.QuestionType, q.QuestionCategoryId,
            CategoryTitle = q.QuestionCategory!.Title, q.LastUpdatedAt
        }).ToListAsync(cancellationToken);
        if (candidates.Count == 0) return new(true, []);

        // Reuse a saved vector only when it describes precisely the captured content.
        // Editing the original must not silently replace the subject of an old mistake.
        var sourceMatches = source.Content == mistake.Content && source.QuestionType == mistake.QuestionType &&
            source.Metadata == mistake.Metadata && source.StandardAnswer == mistake.StandardAnswer &&
            source.Explanation == mistake.Explanation;
        var vector = sourceMatches && source.LastEmbeddedAt >= source.LastUpdatedAt
            ? EmbeddingHelper.Deserialize(source.Embedding) : null;
        if (vector is not { Length: 1024 } || vector.Any(v => !float.IsFinite(v)) || !vector.Any(v => v != 0))
            vector = await search.TryEmbedQueryAsync(
                $"{mistake.QuestionType}\n{mistake.Content}\n{mistake.Metadata}\n{mistake.StandardAnswer}\n{mistake.Explanation}",
                cancellationToken);
        if (vector == null) return new(false, []);

        var ranked = candidates.Where(q => index[q.Id].UpdatedAt == q.LastUpdatedAt)
            .Select(q => new { Question = q, Vector = index[q.Id].Vector,
                Score = EmbeddingHelper.CosineSimilarity(vector, index[q.Id].Vector) })
            .Where(q => q.Score >= MinimumSimilarity && q.Score < DuplicateSimilarity)
            .OrderByDescending(q => q.Question.QuestionCategoryId == source.QuestionCategoryId)
            .ThenByDescending(q => q.Score).ThenBy(q => q.Question.Id);
        var seenText = new HashSet<string> { NormalizeStem(mistake.Content), NormalizeStem(source.Content) };
        var selectedVectors = new List<float[]>();
        var results = new List<RelatedQuestionDto>();
        var hasLongAnswer = false;
        foreach (var candidate in ranked)
        {
            var q = candidate.Question;
            var longAnswer = q.QuestionType is QuestionType.ShortAnswer or QuestionType.Essay;
            var stem = NormalizeStem(q.Content);
            if (stem.Length == 0 || seenText.Contains(stem) || longAnswer && hasLongAnswer ||
                selectedVectors.Any(v => EmbeddingHelper.CosineSimilarity(v, candidate.Vector) >= DuplicateSimilarity))
                continue;
            results.Add(new(q.Id, q.Content, q.QuestionType, q.QuestionCategoryId, q.CategoryTitle,
                q.QuestionCategoryId == source.QuestionCategoryId));
            selectedVectors.Add(candidate.Vector);
            seenText.Add(stem);
            hasLongAnswer |= longAnswer;
            if (results.Count == MaximumResults) break;
        }
        return new(true, results);
    }

    private static string NormalizeStem(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
