using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models;

public class CategoryDto
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public Child[] Children { get; set; } = Array.Empty<Child>();
}

public class Child
{
    public Guid Id { get; set; }
    
    public string Title { get; set; } = string.Empty;
}

public class CreateCategoryDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示创建顶级分类。
    /// </summary>
    public Guid? ParentId { get; set; }
}

public class UpdateCategoryDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 父分类ID。若为 null，表示将分类移动到顶级。
    /// </summary>
    public Guid? ParentId { get; set; }
}