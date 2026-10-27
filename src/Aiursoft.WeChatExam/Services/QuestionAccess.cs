using System.Linq.Expressions;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Practice access is owned by a question category and its course membership.
/// Product sale availability never revokes an existing active membership.
/// Legacy tag-navigation metadata remains separate until its consumers are migrated.
/// </summary>
public static class QuestionAccess
{
    // Course practice uses the mandatory period, never legacy question subjects or tags.
    // Paper snapshots have their own paper-context authorization and do not use this rule.
    public static bool CanPractice(QuestionCategory category, PaperAccessStatus status) =>
        category.IsFree || status.ActiveCategoryVips.Contains(category.TotalCategoryId);

    public static Expression<Func<Question, bool>> PracticeFilter(PaperAccessStatus status) =>
        question => question.QuestionCategory!.IsFree ||
                    status.ActiveCategoryVips.Contains(question.QuestionCategory.TotalCategoryId);

    // Legacy tag-navigation/diagnostic consumers remain until their separate migration.
    public static bool CanUseTag(Tag tag, PaperAccessStatus status) =>
        tag.IsFree || (tag.Taxonomy?.CategoryTaxonomies.Any(c => status.ActiveCategoryVips.Contains(c.CategoryId)) ?? false);

}
