using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

// The recommendation is a similarity-based question suggestion, not a predicted failure probability.
public record BlindSpotCandidateDto(Guid QuestionId, string Content, QuestionType QuestionType,
    Guid QuestionCategoryId, string QuestionCategoryTitle, Guid SourceMistakeId);

public record BlindSpotPreviewDto(bool Available, bool HasUsableHistory,
    IReadOnlyList<BlindSpotCandidateDto> Items);
