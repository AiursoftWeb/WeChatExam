using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class PaperSnapshotDto
{
    public Guid Id { get; set; }
    public Guid? PaperId { get; set; }
    public int Version { get; set; }
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    [Required]
    public int TimeLimit { get; set; }
    public bool IsFree { get; set; }
    public List<QuestionSnapshotDto> Questions { get; set; } = new();
}

public class QuestionSnapshotDto
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public int Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType QuestionType { get; set; }
    public string Metadata { get; set; } = string.Empty;
}

public class SnapshotListDto
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public DateTime CreationTime { get; set; }
}
