using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 表示一条考试资讯或热点知识。
/// </summary>
public class Article
{
    [Key]
    public Guid Id { get; init; }

    [Required]
    [MaxLength(200)]
    public required string Title { get; set; }

    [Required]
    public required string Content { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    public required string AuthorId { get; set; }

    [JsonIgnore]
    [ForeignKey(nameof(AuthorId))]
    public User? Author { get; set; }
}
