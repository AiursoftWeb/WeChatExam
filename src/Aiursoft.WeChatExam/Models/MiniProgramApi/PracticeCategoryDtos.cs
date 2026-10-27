namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public record TotalCategorySummaryDto(Guid Id, string Title, int OrderIndex);

/// <summary>
/// Access describes the course practice entitlement, not a purchase or a content grant.
/// Internal tags and knowledge-directory nodes are deliberately absent from this navigation.
/// </summary>
public record QuestionCategorySummaryDto(
    Guid Id, Guid TotalCategoryId, string Title, int OrderIndex, bool IsFree, bool HasAccess);
