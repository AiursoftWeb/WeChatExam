using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.DTOs;

/// <summary>
/// DTO for importing articles and extracting questions
/// </summary>
public class ArticleImportDto
{
    /// <summary>
    /// Article title
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>
    /// Article content to extract questions from
    /// </summary>
    [Required]
    public required string Content { get; set; }

    /// <summary>
    /// Target category ID for extracted questions
    /// </summary>
    [Required]
    public required Guid CategoryId { get; set; }

    /// <summary>
    /// Optional tags to apply to extracted questions
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Whether to process import asynchronously using background jobs
    /// </summary>
    public bool UseBackgroundJob { get; set; } = true;
}

/// <summary>
/// DTO for extracted question data
/// </summary>
public class ExtractedQuestionDto
{
    /// <summary>
    /// Question content/text
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public required string Content { get; set; }

    /// <summary>
    /// Question type (Choice, Blank, Bool, ShortAnswer, Essay)
    /// </summary>
    [Required]
    public QuestionType QuestionType { get; set; }

    /// <summary>
    /// Grading strategy (ExactMatch, FuzzyMatch, AiEval)
    /// </summary>
    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    /// <summary>
    /// Question metadata (JSON for choices, etc.)
    /// </summary>
    [MaxLength(5000)]
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// Standard answer/correct answer
    /// </summary>
    [MaxLength(5000)]
    public string StandardAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Question explanation
    /// </summary>
    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Extracted tags for this question
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Result of article import operation
/// </summary>
public class ArticleImportResultDto
{
    /// <summary>
    /// ID of the created article
    /// </summary>
    public Guid ArticleId { get; set; }

    /// <summary>
    /// Number of questions extracted
    /// </summary>
    public int QuestionsExtracted { get; set; }

    /// <summary>
    /// IDs of created questions
    /// </summary>
    public List<Guid> QuestionIds { get; set; } = new();

    /// <summary>
    /// Background job ID if processed asynchronously
    /// </summary>
    public Guid? BackgroundJobId { get; set; }

    /// <summary>
    /// Processing status
    /// </summary>
    public ImportStatus Status { get; set; }

    /// <summary>
    /// Any error messages
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Import status enumeration
/// </summary>
public enum ImportStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

/// <summary>
/// DTO for content extraction requests
/// </summary>
public class ContentExtractionRequestDto
{
    /// <summary>
    /// Content to extract from
    /// </summary>
    [Required]
    public required string Content { get; set; }
}