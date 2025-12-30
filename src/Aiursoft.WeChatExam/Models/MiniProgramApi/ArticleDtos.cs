using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

/// <summary>
/// 资讯信息的 DTO
/// </summary>
public class ArticleDto
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }

    public string AuthorName { get; set; } = string.Empty;
}
