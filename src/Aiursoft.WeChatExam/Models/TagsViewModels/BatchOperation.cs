using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.TagsViewModels;

/// <summary>
/// Request model for batch delete operation
/// </summary>
public class BatchDeleteRequest
{
    /// <summary>
    /// List of tag IDs to delete
    /// </summary>
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Tag IDs")]
    public required int[] TagIds { get; init; } = Array.Empty<int>();
}

/// <summary>
/// Response model for batch delete operation
/// </summary>
public class BatchDeleteResult
{
    /// <summary>
    /// Number of successfully deleted tags
    /// </summary>
    public int DeletedCount { get; set; }
    
    /// <summary>
    /// IDs of successfully deleted tags
    /// </summary>
    public int[] DeletedIds { get; set; } = Array.Empty<int>();
}
