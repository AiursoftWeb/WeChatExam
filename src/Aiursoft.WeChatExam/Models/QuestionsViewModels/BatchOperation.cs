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
    [Required]
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
