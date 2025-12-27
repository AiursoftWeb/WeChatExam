using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class Category
{
    [Key]
    public Guid Id { get; init; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 父分类ID。
    /// 若为 null，表示这是顶级分类。
    /// </summary>
    public required Guid? ParentId { get; set; }

    // 导航引用：Category?, JsonIgnore, ForeignKey
    [JsonIgnore]
    [ForeignKey(nameof(ParentId))]
    public Category? Parent { get; set; }

    [InverseProperty(nameof(Parent))]
    public IEnumerable<Category> Children { get; init; } = new List<Category>();

    [InverseProperty(nameof(Question.Category))]
    public IEnumerable<Question> Questions { get; init; } = new List<Question>();

    [InverseProperty(nameof(CategoryKnowledgePoint.Category))]
    public IEnumerable<CategoryKnowledgePoint> CategoryKnowledgePoints { get; init; } = new List<CategoryKnowledgePoint>();
}