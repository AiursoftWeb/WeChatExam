using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// A mock paper and every selected question must share one course. Validate both sides
/// before edits: moving a question or a whole period can otherwise break an existing paper.
/// Free mock papers may include paid questions; opening the paper is an explicit arrangement.
/// </summary>
public class MockPaperValidation(WeChatExamDbContext context, IStringLocalizer<MockPaperValidation> localizer)
{
    public async Task<string?> ValidatePaperCourseAsync(Guid paperId, Guid courseId)
    {
        // An existing version must keep its course entitlement even if its live questions are removed.
        if (await context.PaperSnapshots.AnyAsync(s => s.MockPaperId == paperId && s.MockPaper!.TotalCategoryId != courseId))
            return localizer["A published paper cannot change courses. Create another mock paper for the new course."];
        var link = await context.MockPaperQuestions.AsNoTracking()
            .Where(l => l.MockPaperId == paperId && l.Question.QuestionCategory!.TotalCategoryId != courseId)
            .OrderBy(l => l.QuestionId).Select(l => new { l.MockPaper.Title, l.Question.Content }).FirstOrDefaultAsync();
        return link == null ? null : CourseConflict(link.Title, link.Content);
    }

    public async Task<string?> ValidateQuestionPeriodsAsync(IReadOnlyDictionary<Guid, Guid> assignments)
    {
        var periodIds = assignments.Values.Distinct().ToArray();
        var courses = await context.QuestionCategories.AsNoTracking().Where(c => periodIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.TotalCategoryId);
        var ids = assignments.Keys.ToArray();
        var links = await context.MockPaperQuestions.AsNoTracking().Where(l => ids.Contains(l.QuestionId))
            .OrderBy(l => l.MockPaperId).ThenBy(l => l.QuestionId)
            .Select(l => new { l.QuestionId, l.Question.Content, l.MockPaper.TotalCategoryId, l.MockPaper.Title }).ToListAsync();
        var conflict = links.FirstOrDefault(l => !courses.TryGetValue(assignments[l.QuestionId], out var course) || course != l.TotalCategoryId);
        return conflict == null ? null : CourseConflict(conflict.Title, conflict.Content);
    }

    public async Task<string?> ValidatePeriodCourseAsync(Guid periodId, Guid courseId)
    {
        var link = await context.MockPaperQuestions.AsNoTracking()
            .Where(l => l.Question.QuestionCategoryId == periodId && l.MockPaper.TotalCategoryId != courseId)
            .OrderBy(l => l.QuestionId).Select(l => new { l.MockPaper.Title, l.Question.Content }).FirstOrDefaultAsync();
        return link == null ? null : CourseConflict(link.Title, link.Content);
    }

    public async Task<string?> ValidateDeletionAsync(IEnumerable<Guid> questionIds)
    {
        var link = await context.MockPaperQuestions.AsNoTracking().Where(l => questionIds.Contains(l.QuestionId))
            .OrderBy(l => l.MockPaperId).ThenBy(l => l.QuestionId)
            .Select(l => new { l.MockPaper.Title, l.Question.Content }).FirstOrDefaultAsync();
        return link == null ? null : string.Format(localizer["Question {0} is used by mock paper {1}. Remove it from the paper before deleting it."].Value,
            Preview(link.Content), link.Title);
    }

    private string CourseConflict(string paper, string question) => string.Format(localizer["Mock paper {0} and question {1} must belong to the same course. Remove the question from the paper before changing its course."].Value,
        paper, Preview(question));

    private static string Preview(string text) => text.Length > 100 ? text[..100] + "…" : text;
}
