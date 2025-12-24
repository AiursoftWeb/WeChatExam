using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi.KnowledgePointDtos;

/// <summary>
/// 知识点树状结构 DTO，支持任意深度的层级嵌套
/// </summary>
public class KnowledgePointDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 子知识点列表，支持递归嵌套
    /// </summary>
    public List<KnowledgePointDto> Children { get; set; } = new List<KnowledgePointDto>();
}


public class KnowledgeDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;
    
    [Required]
    public string Content { get; set; } = string.Empty;

}

