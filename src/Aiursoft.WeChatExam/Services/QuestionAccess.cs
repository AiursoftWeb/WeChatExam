using System.Linq.Expressions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Practice access is owned by a question category and its course membership.
/// Product sale availability never revokes an existing active membership.
/// </summary>
public static class QuestionAccess
{
    // Course practice uses the mandatory period, never legacy question subjects or tags.
    // Paper snapshots have their own paper-context authorization and do not use this rule.
    public static bool CanPractice(QuestionCategory category, PaperAccessStatus status) =>
        category.IsFree || status.ActiveCategoryVips.Contains(category.TotalCategoryId);

    public static bool CanBrowse(QuestionCategory category, PaperAccessStatus status) =>
        category.IsVisible && CanPractice(category, status);

    public static Task<bool> HasKnowledgeContextAsync(WeChatExamDbContext context, Guid questionId,
        Guid knowledgePointId, PaperAccessStatus status) => context.KnowledgePointQuestions.AsNoTracking()
        .AnyAsync(link => link.QuestionId == questionId && link.KnowledgePointId == knowledgePointId &&
            link.KnowledgePoint.KnowledgeCategoryKnowledgePoints.Any(placement =>
                placement.KnowledgeCategory.IsFree || status.ActiveCategoryVips.Contains(placement.KnowledgeCategory.TotalCategoryId)));

    public static Expression<Func<Question, bool>> PracticeFilter(PaperAccessStatus status) =>
        question => question.QuestionCategory!.IsVisible && (question.QuestionCategory.IsFree ||
                    status.ActiveCategoryVips.Contains(question.QuestionCategory.TotalCategoryId));

}
