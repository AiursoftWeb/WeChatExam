using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public enum QuestionAccessDenial { None, Anonymous, Subject, Tag }

public record QuestionAccessDecision(QuestionAccessDenial Denial, Tag? BlockingTag = null)
{
    public bool Allowed => Denial == QuestionAccessDenial.None;
}

/// <summary>
/// Shared by the serving endpoints and configuration diagnostics. Product availability is
/// deliberately absent: disabling a product does not revoke an existing active membership.
/// Callers must supply loaded tag taxonomy mappings and an active membership status.
/// </summary>
public static class QuestionAccess
{
    public static bool CanUseTag(Tag tag, PaperAccessStatus status) =>
        tag.IsFree || (tag.Taxonomy?.CategoryTaxonomies.Any(c => status.ActiveCategoryVips.Contains(c.CategoryId)) ?? false);

    public static QuestionAccessDecision Evaluate(Question question, bool authenticated, PaperAccessStatus status)
    {
        var paidTags = question.QuestionTags.Select(t => t.Tag).Where(t => !t.IsFree).ToList();
        if (paidTags.Count == 0) return new(QuestionAccessDenial.None);
        if (!authenticated) return new(QuestionAccessDenial.Anonymous);

        // A subject VIP includes all paid questions assigned to that subject. Descriptive
        // tags only mark them as paid; their mappings do not impose additional subscriptions.
        if (question.CategoryId is { } subject)
            return new(status.ActiveCategoryVips.Contains(subject) ? QuestionAccessDenial.None : QuestionAccessDenial.Subject);

        // Legacy uncategorized questions require every paid tag, with alternative subjects
        // within each tag. An unmapped paid tag cannot be satisfied even by all VIPs.
        var blocked = paidTags.FirstOrDefault(t => !CanUseTag(t, status));
        return blocked == null ? new(QuestionAccessDenial.None) : new(QuestionAccessDenial.Tag, blocked);
    }
}
