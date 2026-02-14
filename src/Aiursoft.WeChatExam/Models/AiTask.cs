using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models;

public enum AiTaskStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public class AiTaskItem
{
    public Guid QuestionId { get; set; }
    public string QuestionContent { get; set; } = string.Empty;
    public string QuestionStandardAnswer { get; set; } = string.Empty;
    public string OldExplanation { get; set; } = string.Empty;
    public string NewExplanation { get; set; } = string.Empty;
    public AiTaskStatus Status { get; set; }
    public string? Error { get; set; }
}

public class AiTask : UiStackLayoutViewModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<Guid, AiTaskItem> Items { get; set; } = new();
    public bool IsCompleted => Items.Values.All(i => i.Status == AiTaskStatus.Completed || i.Status == AiTaskStatus.Failed);
}