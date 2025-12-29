using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;


namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class KnowledgePointWithQuestionsDto
{
    public Guid Id { get; set; }
    [Required]
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public List<QuestionContentDto> Questions { get; set; } = new();
}


public class QuestionContentDto
{
    public Guid Id { get; set; }
    [Required]
    public QuestionType QuestionType { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Metadata { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
