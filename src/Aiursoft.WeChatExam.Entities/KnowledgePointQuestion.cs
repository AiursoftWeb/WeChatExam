using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class KnowledgePointQuestion
{
    [Key]
    public Guid Id { get; init; }

    public required Guid KnowledgePointId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(KnowledgePointId))]
    [NotNull]
    public KnowledgePoint? KnowledgePoint { get; set; }

    public required Guid QuestionId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(QuestionId))]
    [NotNull]
    public Question? Question { get; set; }
}
