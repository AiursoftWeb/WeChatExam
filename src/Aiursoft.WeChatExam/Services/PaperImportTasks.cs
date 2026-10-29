using Aiursoft.WeChatExam.Models.PapersViewModels;

namespace Aiursoft.WeChatExam.Services;

public class PaperImportTask
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string OwnerId { get; init; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
    public string? Error { get; set; }
    public PaperImportPreview? Preview { get; set; }
    public Guid? PaperId { get; set; }
    public ImportPaperKind? Kind { get; set; }
    public SemaphoreSlim SaveLock { get; } = new(1, 1);
}

// Bounded, process-local previews: a restart deliberately requires fresh extraction.
public class PaperImportTasks
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, PaperImportTask> _tasks = [];
    private void Cleanup()
    {
        foreach (var id in _tasks.Values.Where(t => t.CreatedAt < DateTime.UtcNow.AddHours(-2)).Select(t => t.Id).ToArray())
            _tasks.Remove(id);
    }
    public PaperImportTask Create(string owner)
    {
        lock (_gate)
        {
            Cleanup();
            if (_tasks.Count >= 500 || _tasks.Values.Count(t => t.OwnerId == owner && t.Status is "Pending" or "Processing") >= 2)
                throw new InvalidOperationException("Too many import tasks. Wait for the current extraction to finish.");
            var task = new PaperImportTask { OwnerId = owner };
            _tasks.Add(task.Id, task);
            return task;
        }
    }
    public PaperImportTask? Get(Guid id, string owner)
    {
        lock (_gate)
        {
            Cleanup();
            return _tasks.TryGetValue(id, out var task) && task.OwnerId == owner ? task : null;
        }
    }
}
