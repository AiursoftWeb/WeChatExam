using System.Collections.Concurrent;
using Aiursoft.WeChatExam.Models;

namespace Aiursoft.WeChatExam.Services;

public class AiTaskService
{
    private readonly ConcurrentDictionary<Guid, AiTask> _tasks = new();

    public AiTask CreateTask(IEnumerable<AiTaskItem> items)
    {
        var task = new AiTask();
        foreach (var item in items)
        {
            task.Items.TryAdd(item.QuestionId, item);
        }
        _tasks.TryAdd(task.Id, task);

        // Cleanup old tasks (older than 24 hours)
        var oldTaskIds = _tasks.Values
            .Where(t => t.CreatedAt < DateTime.UtcNow.AddDays(-1))
            .Select(t => t.Id)
            .ToList();
        
        foreach (var id in oldTaskIds)
        {
            _tasks.TryRemove(id, out _);
        }

        return task;
    }

    public AiTask? GetTask(Guid id)
    {
        _tasks.TryGetValue(id, out var task);
        return task;
    }
}