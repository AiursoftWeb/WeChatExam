using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ExtractViewModels;

public class ExtractedKnowledgePoint
{
    public string KnowledgeTitle { get; set; } = string.Empty;
    public string KnowledgeContent { get; set; } = string.Empty;
    public List<ExtractedQuestion> Questions { get; set; } = new();
}

public class ExtractedQuestion
{
    public string QuestionContent { get; set; } = string.Empty;
    
    public QuestionType QuestionType { get; set; }
    
    public List<string> Metadata { get; set; } = new();
    
    public string StandardAnswer { get; set; } = string.Empty;
    
    public string Explanation { get; set; } = string.Empty;
    
    public List<string> Tags { get; set; } = new();
}
