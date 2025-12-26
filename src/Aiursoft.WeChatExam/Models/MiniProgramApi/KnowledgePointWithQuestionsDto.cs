using System;
using System.Collections.Generic;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class KnowledgePointWithQuestionsDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public List<QuestionContentDto> Questions { get; set; } = new();
}


public class QuestionContentDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string List { get; set; } = string.Empty;
    public string SingleCorrect { get; set; } = string.Empty;
    public string FillInCorrect { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
