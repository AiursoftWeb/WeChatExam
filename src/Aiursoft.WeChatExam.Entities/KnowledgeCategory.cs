using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>A knowledge directory within one course. Question periods and institutions are separate entities.</summary>
public class KnowledgeCategory
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    public required Guid TotalCategoryId { get; set; }
    [JsonIgnore]
    public TotalCategory? TotalCategory { get; set; }

    public Guid? ParentKnowledgeCategoryId { get; set; }
    [JsonIgnore]
    public KnowledgeCategory? ParentKnowledgeCategory { get; set; }

    [InverseProperty(nameof(ParentKnowledgeCategory))]
    public ICollection<KnowledgeCategory> Children { get; set; } = new List<KnowledgeCategory>();

    public bool IsFree { get; set; }
    public int OrderIndex { get; set; }
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    public ICollection<KnowledgeCategoryKnowledgePoint> KnowledgeCategoryKnowledgePoints { get; set; } = new List<KnowledgeCategoryKnowledgePoint>();
}
