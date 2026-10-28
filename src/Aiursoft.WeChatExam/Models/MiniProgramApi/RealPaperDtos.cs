namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public record RealPaperInstitutionDto(Guid Id, string Title, int PaperCount, bool HasAccess);

public record RealPaperSummaryDto(Guid Id, Guid InstitutionId, string Title, int TimeLimit,
    bool HasAccess, Guid LatestSnapshotId, int LatestVersion);

public record RealPaperSnapshotDto(Guid Id, Guid RealPaperId, Guid InstitutionId, int Version,
    string Title, int TimeLimit, List<QuestionSnapshotDto> Questions);
