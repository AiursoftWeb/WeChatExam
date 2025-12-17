using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models;

public class CategoryDto
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Title { get; set; } = string.Empty;

    public Child[] Children { get; set; } = Array.Empty<Child>();
}

public class Child
{
    public string Id { get; set; } = string.Empty;
    
    public string Title { get; set; } = string.Empty;
}

public class CreateCategoryDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ParentId { get; set; }
}

public class UpdateCategoryDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ParentId { get; set; }
}