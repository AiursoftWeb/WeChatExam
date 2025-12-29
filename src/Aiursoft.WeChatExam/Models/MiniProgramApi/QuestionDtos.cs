using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class QuestionDto
{
    [Required]
    public QuestionType QuestionType { get; set; }
    
    [Required]
    public Value Value { get; set; } = new();
}

public class Value
{
    public Guid Id { get; set; }
    
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;
    
    public string Metadata { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// 创建题目的 DTO
/// </summary>
public class CreateQuestionDto
{
    [Required]
    public QuestionType QuestionType { get; set; }

    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Metadata (Choices, Logic, etc.) - JSON
    /// </summary>
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Standard Answer / Grading Logic
    /// </summary>
    public string StandardAnswer { get; set; } = string.Empty;

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
    public QuestionType QuestionType { get; set; }

    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Content { get; set; } = string.Empty;

    public string Metadata { get; set; } = string.Empty;

    public string StandardAnswer { get; set; } = string.Empty;

    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }
}

