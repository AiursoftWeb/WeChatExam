using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

// Similarity selects practice candidates; it is not a predicted probability of answering incorrectly.
public record RelatedQuestionDto(Guid Id, string Content, QuestionType QuestionType,
    Guid QuestionCategoryId, string QuestionCategoryTitle, bool SameQuestionCategory);

public record RelatedQuestionsDto(bool Available, IReadOnlyList<RelatedQuestionDto> Items);
