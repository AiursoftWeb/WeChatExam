using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.ContentChecksViewModels;

public enum CheckSeverity { Error, Warning, Information }

public record ContentReference(string Controller, string Id, string Name, string Permission);
public record AccessObservation(string Scenario, string Entry, bool Allowed, string Reason);
public record AccessScenario(string Name, bool Authenticated, PaperAccessStatus Status);

public class ContentFinding
{
    public required string Rule { get; init; }
    public required CheckSeverity Severity { get; init; }
    public bool NeedsEngineering { get; init; }
    public required string Title { get; init; }
    public required string Reason { get; init; }
    public required string Suggestion { get; init; }
    public List<ContentReference> Objects { get; } = [];
    public HashSet<Guid> CategoryIds { get; } = [];
    public List<AccessObservation> Observations { get; } = [];
}

public class ContentCheckReport
{
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime CompletedAt { get; set; }
    public Dictionary<string, int> Counts { get; } = new();
    public List<Category> Categories { get; set; } = [];
    public List<ContentFinding> Findings { get; } = [];
    public List<AccessScenario> Scenarios { get; } = [];
    public Dictionary<int, List<ContentReference>> TagImpacts { get; } = new();
}

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Content Configuration Checks";
    public ContentCheckReport? Report { get; set; }
    public List<ContentFinding> Results { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public const int PageSize = 50;
    public string? Severity { get; set; }
    public string? Rule { get; set; }
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid[] Subjects { get; set; } = [];
    public bool RealExam { get; set; }
    public HashSet<string> EditPermissions { get; set; } = [];
}
