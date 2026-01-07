using System.ComponentModel.DataAnnotations;


namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class KnowledgePointWithQuestionsDto
{
    public Guid Id { get; set; }
    [Required]
    public string Text { get; set; } = string.Empty;
    public string? AudioUrl { get; set; }
    public List<QuestionContentDto> Questions { get; set; } = new();
}


public class QuestionContentDto
{
    public Guid Id { get; set; }
    [Required]
    public string Text { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}
