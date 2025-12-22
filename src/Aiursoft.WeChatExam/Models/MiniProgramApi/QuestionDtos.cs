using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class QuestionDto
{
    [Required]
    public string Type { get; set; } = string.Empty;
    
    [Required]
    public Value Value { get; set; } = new();
}

public class Value
{
    public Guid Id { get; set; }
    
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
    
    public string[] List { get; set; } = Array.Empty<string>();

    public string SingleCorrect { get; set; } = string.Empty;

    public string[] FillInCorrect { get; set; } = Array.Empty<string>();

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// 创建题目的 DTO
/// </summary>
public class CreateQuestionDto
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 单选题选项列表，JSON 格式的字符串
    /// </summary>
    public string List { get; set; } = string.Empty;

    /// <summary>
    /// 单选题的正确答案，填空题为空字符串
    /// </summary>
    public string SingleCorrect { get; set; } = string.Empty;

    /// <summary>
    /// 填空题的正确答案数组，JSON 格式的字符串
    /// </summary>
    public string FillInCorrect { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }
}

/// <summary>
/// 更新题目的 DTO
/// </summary>
public class UpdateQuestionDto
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;

    public string List { get; set; } = string.Empty;

    public string SingleCorrect { get; set; } = string.Empty;

    public string FillInCorrect { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }
}

