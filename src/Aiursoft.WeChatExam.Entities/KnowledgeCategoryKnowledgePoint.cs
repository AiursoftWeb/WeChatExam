using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class KnowledgeCategoryKnowledgePoint
{
    [Key]
    public Guid Id { get; init; }

    public required Guid KnowledgeCategoryId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(KnowledgeCategoryId))]
    [NotNull]
    public KnowledgeCategory? KnowledgeCategory { get; set; }

    public required Guid KnowledgePointId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(KnowledgePointId))]
    [NotNull]
    public KnowledgePoint? KnowledgePoint { get; set; }

    /// <summary>
    /// Display order of this knowledge point within the associated category.
    /// </summary>
    public int OrderIndex { get; set; }
}
