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
    
    public string? AudioUrl { get; set; }
    
    public required string Content { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 父知识点ID。
    /// 若为 null，表示这是顶级知识点。
    /// </summary>
    public required Guid? ParentId { get; set; }

    // 导航引用：KnowledgePoint?, JsonIgnore, ForeignKey
    [JsonIgnore]
    [ForeignKey(nameof(ParentId))]
    public KnowledgePoint? Parent { get; set; }

    [InverseProperty(nameof(Parent))]
    public IEnumerable<KnowledgePoint> Children { get; init; } = new List<KnowledgePoint>();

    [InverseProperty(nameof(CategoryKnowledgePoint.KnowledgePoint))]
    public IEnumerable<CategoryKnowledgePoint> CategoryKnowledgePoints { get; init; } = new List<CategoryKnowledgePoint>();

    [InverseProperty(nameof(KnowledgePointQuestion.KnowledgePoint))]
    public IEnumerable<KnowledgePointQuestion> KnowledgePointQuestions { get; init; } = new List<KnowledgePointQuestion>();

}