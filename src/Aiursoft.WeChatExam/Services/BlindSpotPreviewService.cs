using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Suggests new, authorized questions using a student's own recent wrong answers as clues.</summary>
public class BlindSpotPreviewService(WeChatExamDbContext db, RelatedQuestionService related) : IScopedDependency
{
    private const int MaximumSeeds = 3;
    private const int MaximumResults = 3;

    public async Task<BlindSpotPreviewDto> FindAsync(string userId, PaperAccessStatus access,
        CancellationToken cancellationToken = default)
    {
        // Keep the scan bounded. Repeated wrong copies of one question count as one clue;
        // an orphaned paper snapshot without a live original has no dependable course scope.
        var recent = await db.MistakeRecords.AsNoTracking().Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreationTime).ThenByDescending(m => m.Id)
            .Take(30).ToListAsync(cancellationToken);
        var seeds = recent.Where(m => m.SourceQuestionId.HasValue)
            .DistinctBy(m => m.SourceQuestionId).ToList();
        if (seeds.Count == 0) return new(true, false, []);
        var sourceIds = seeds.Select(m => m.SourceQuestionId!.Value).ToArray();
        var liveIds = await db.Questions.AsNoTracking().Where(q => sourceIds.Contains(q.Id) && q.QuestionCategory != null)
            .Select(q => q.Id).ToListAsync(cancellationToken);
        seeds = seeds.Where(m => liveIds.Contains(m.SourceQuestionId!.Value)).Take(MaximumSeeds).ToList();
        if (seeds.Count == 0) return new(true, false, []);

        var candidates = new List<BlindSpotCandidateDto>();
        var ids = new HashSet<Guid>();
        var stems = new HashSet<string>();
        var available = true;
        foreach (var seed in seeds)
        {
            var result = await related.FindUnseenAsync(seed, access, userId, cancellationToken);
            available &= result.Available;
            if (!result.Available) break;
            foreach (var item in result.Items)
            {
                var stem = new string(item.Content.Where(char.IsLetterOrDigit)
                    .Select(char.ToUpperInvariant).ToArray());
                if (stem.Length == 0 || !ids.Add(item.Id) || !stems.Add(stem)) continue;
                candidates.Add(new(item.Id, item.Content, item.QuestionType, item.QuestionCategoryId,
                    item.QuestionCategoryTitle, seed.Id));
            }
        }

        // Prefer distinct periods where possible, then fill remaining positions with the
        // strongest per-seed matches. No weak filler or numerical mastery claim is returned.
        var chosen = new List<BlindSpotCandidateDto>();
        var periods = new HashSet<Guid>();
        foreach (var candidate in candidates)
            if (periods.Add(candidate.QuestionCategoryId)) chosen.Add(candidate);
        foreach (var candidate in candidates)
            if (!chosen.Contains(candidate)) chosen.Add(candidate);
        return new(available || chosen.Count > 0, true, chosen.Take(MaximumResults).ToList());
    }
}
