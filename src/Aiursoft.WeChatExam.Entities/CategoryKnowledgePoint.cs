using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class CategoryKnowledgePoint
{
    [Key]
    public Guid Id { get; init; }

    public required Guid CategoryId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    [NotNull]
    public Category? Category { get; set; }

    public required Guid KnowledgePointId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(KnowledgePointId))]
    [NotNull]
    public KnowledgePoint? KnowledgePoint { get; set; }
}
