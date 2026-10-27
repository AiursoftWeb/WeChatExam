namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

/// <summary>Public navigation metadata. Lesson bodies are authorized separately.</summary>
public record KnowledgeCategoryDto(
    Guid Id, Guid TotalCategoryId, string Title, int OrderIndex,
    bool IsFree, bool HasAccess, int KnowledgePointCount, List<KnowledgeCategoryDto> Children);
