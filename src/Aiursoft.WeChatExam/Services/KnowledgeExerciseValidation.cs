using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Free knowledge must have free exercises. Check proposed writes before changing tracked
/// entities; never repair prices or remove associations on the teacher's behalf.
/// Only direct knowledge placements count, matching KnowledgeAccess (no inherited pricing).
/// </summary>
public class KnowledgeExerciseValidation(
    WeChatExamDbContext context,
    IStringLocalizer<KnowledgeExerciseValidation> localizer)
{
    public async Task<string?> ValidateAssociationsAsync(IEnumerable<Guid> categoryIds, IEnumerable<Guid> questionIds)
    {
        var category = await context.KnowledgeCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.Id) && c.IsFree).OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (category == null) return null;
        var question = await context.Questions.AsNoTracking().Include(q => q.QuestionCategory)
            .Where(q => questionIds.Contains(q.Id) && !q.QuestionCategory!.IsFree)
            .OrderBy(q => q.Id).FirstOrDefaultAsync();
        return question == null ? null : Conflict(category.Title, question.Content, question.QuestionCategory!.Title);
    }

    public async Task<string?> ValidateFreeKnowledgeCategoryAsync(Guid id)
    {
        var link = await context.KnowledgePointQuestions.AsNoTracking()
            .Where(k => k.KnowledgePoint.KnowledgeCategoryKnowledgePoints.Any(c => c.KnowledgeCategoryId == id)
                && !k.Question.QuestionCategory!.IsFree)
            .OrderBy(k => k.QuestionId)
            .Select(k => new { k.Question.Content, Period = k.Question.QuestionCategory!.Title }).FirstOrDefaultAsync();
        if (link == null) return null;
        var title = await context.KnowledgeCategories.Where(c => c.Id == id).Select(c => c.Title).SingleAsync();
        return Conflict(title, link.Content, link.Period);
    }

    public async Task<string?> ValidatePaidQuestionCategoryAsync(Guid id)
    {
        var link = await context.KnowledgePointQuestions.AsNoTracking()
            .Where(k => k.Question.QuestionCategoryId == id)
            .SelectMany(k => k.KnowledgePoint.KnowledgeCategoryKnowledgePoints
                .Where(c => c.KnowledgeCategory.IsFree)
                .Select(c => new { Category = c.KnowledgeCategory.Title, k.Question.Content,
                    Period = k.Question.QuestionCategory!.Title }))
            .OrderBy(k => k.Category).ThenBy(k => k.Content).FirstOrDefaultAsync();
        return link == null ? null : Conflict(link.Category, link.Content, link.Period);
    }

    public async Task<string?> ValidateQuestionPeriodsAsync(IReadOnlyDictionary<Guid, Guid> assignments)
    {
        var periodIds = assignments.Values.Distinct().ToArray();
        var paidPeriods = await context.QuestionCategories.AsNoTracking()
            .Where(c => periodIds.Contains(c.Id) && !c.IsFree).ToDictionaryAsync(c => c.Id, c => c.Title);
        var questionIds = assignments.Where(a => paidPeriods.ContainsKey(a.Value)).Select(a => a.Key).ToArray();
        if (questionIds.Length == 0) return null;
        // Existing conflicts are not silently repaired. An unchanged period does not
        // prevent unrelated question edits; newly introduced conflicts are rejected.
        var current = await context.Questions.Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.QuestionCategoryId }).ToListAsync();
        var changed = current.Where(q => q.QuestionCategoryId != assignments[q.Id]).Select(q => q.Id).ToArray();
        var link = await context.KnowledgePointQuestions.AsNoTracking()
            .Where(k => changed.Contains(k.QuestionId))
            .SelectMany(k => k.KnowledgePoint.KnowledgeCategoryKnowledgePoints
                .Where(c => c.KnowledgeCategory.IsFree)
                .Select(c => new { Category = c.KnowledgeCategory.Title, k.QuestionId, k.Question.Content }))
            .OrderBy(k => k.QuestionId).ThenBy(k => k.Category).FirstOrDefaultAsync();
        return link == null ? null : Conflict(link.Category, link.Content, paidPeriods[assignments[link.QuestionId]]);
    }

    public async Task<string?> ValidateExtractionAsync(Guid knowledgeCategoryId, Guid questionCategoryId, CancellationToken token = default)
    {
        var category = await context.KnowledgeCategories.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == knowledgeCategoryId && c.IsFree, token);
        if (category == null) return null;
        var period = await context.QuestionCategories.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == questionCategoryId && !c.IsFree, token);
        return period == null ? null : string.Format(localizer["Free knowledge category {0} cannot use exercises from paid question category {1}. Choose a free question category or review the knowledge access settings."].Value,
            category.Title, period.Title);
    }

    private string Conflict(string category, string question, string period) => string.Format(localizer["Free knowledge category {0} cannot use exercise {1} from paid question category {2}. Choose a free exercise or review the knowledge access settings."].Value,
        category, question.Length > 100 ? question[..100] + "…" : question, period);
}
