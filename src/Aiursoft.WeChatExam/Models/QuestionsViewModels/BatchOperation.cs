using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.QuestionsViewModels;

/// <summary>
/// Request model for batch delete operation
/// </summary>
public class BatchDeleteRequest
{
    /// <summary>
    /// List of question IDs to delete
    /// </summary>
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Question IDs")]
    public required Guid[] QuestionIds { get; init; } = Array.Empty<Guid>();
}

/// <summary>
/// Response model for batch delete operation
/// </summary>
public class BatchDeleteResult
{
    /// <summary>
    /// Number of successfully deleted questions
    /// </summary>
    public int DeletedCount { get; set; }
    
    /// <summary>
    /// IDs of successfully deleted questions
    /// </summary>
    public Guid[] DeletedIds { get; set; } = Array.Empty<Guid>();
}

/// <summary>
/// Request model for batch AI classification operation
/// </summary>
public class BatchAiClassifyRequest
{
    /// <summary>
    /// List of question IDs to classify
    /// </summary>
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Question IDs")]
    public required Guid[] QuestionIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// List of candidate category IDs for AI to choose from
    /// </summary>
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Category IDs")]
    public required Guid[] CategoryIds { get; init; } = Array.Empty<Guid>();
}

/// <summary>
/// Response model for batch AI classification operation
/// </summary>
public class BatchAiClassifyResult
{
    /// <summary>
    /// Number of classification jobs enqueued
    /// </summary>
    public int EnqueuedCount { get; set; }

    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}

