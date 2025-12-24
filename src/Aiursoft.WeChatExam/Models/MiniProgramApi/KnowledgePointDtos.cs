using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi.KnowledgePointDtos;

public class KnowledgePointDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public Child[] Children { get; set; } = Array.Empty<Child>();
}

public class Child
{
    public Guid Id { get; set; }
    
    public string Title { get; set; } = string.Empty;
}

