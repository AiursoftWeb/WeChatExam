using System.Collections.Concurrent;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models;

public enum AiTaskStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public enum AiTaskType
{
    GenerateExplanation,
    AutoCategorize,
    AutoTagging
}

public class AiTaskItem
{
    public Guid QuestionId { get; set; }
    public string QuestionContent { get; set; } = string.Empty;
    public string QuestionStandardAnswer { get; set; } = string.Empty;
    
    /// <summary>
    /// For Explanation task: Old Explanation
    /// For Categorize task: Old Category Name
    /// </summary>
    public string OldValue { get; set; } = string.Empty;

    /// <summary>
    /// For Explanation task: New Explanation
    /// For Categorize task: New Category Name
    /// </summary>
    public string NewValue { get; set; } = string.Empty;
    
    /// <summary>
    /// For Categorize task: New Category Id
    /// </summary>
    public Guid? NewEntityId { get; set; }
    
    public AiTaskStatus Status { get; set; }
    public string? Error { get; set; }
}

public class AiTask : UiStackLayoutViewModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public AiTaskType Type { get; set; }
    public ConcurrentDictionary<Guid, AiTaskItem> Items { get; set; } = new();
    public bool IsCompleted => Items.Values.All(i => i.Status == AiTaskStatus.Completed || i.Status == AiTaskStatus.Failed);
}