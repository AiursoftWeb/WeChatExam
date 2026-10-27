using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Each directory owns access to the lessons placed directly inside it. Tree parents
/// organize navigation; they do not grant access to a paid child directory.
/// </summary>
public static class KnowledgeAccess
{
    public static bool CanRead(KnowledgeCategory category, PaperAccessStatus status) =>
        category.IsFree || status.ActiveCategoryVips.Contains(category.TotalCategoryId);
}
