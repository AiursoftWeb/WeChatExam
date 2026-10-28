namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public record MockPaperSummaryDto(Guid Id, Guid TotalCategoryId, string Title, int TimeLimit,
    bool AllowViewAnonymousely, bool HasAccess, Guid LatestSnapshotId, int LatestVersion);

public record MockPaperSnapshotDto(Guid Id, Guid MockPaperId, Guid TotalCategoryId, int Version,
    string Title, int TimeLimit, bool AllowViewAnonymousely, List<QuestionSnapshotDto> Questions);
