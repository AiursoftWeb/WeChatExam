using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class KnowledgePoint
{
    [Key]
    public Guid Id { get; init; }

    [MaxLength(200)]
    public required string Title { get; set; }
    
    [MaxLength(1000)]
    public string? AudioUrl { get; set; }
    
    [MaxLength(10000)]
    public required string Content { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>The last change to content used for embedding generation.</summary>
    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? Embedding { get; set; }

    [JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime LastEmbeddedAt { get; set; } = DateTime.MinValue;

    [InverseProperty(nameof(CategoryKnowledgePoint.KnowledgePoint))]
    public IEnumerable<CategoryKnowledgePoint> CategoryKnowledgePoints { get; init; } = new List<CategoryKnowledgePoint>();

    [InverseProperty(nameof(KnowledgePointQuestion.KnowledgePoint))]
    public IEnumerable<KnowledgePointQuestion> KnowledgePointQuestions { get; init; } = new List<KnowledgePointQuestion>();

}