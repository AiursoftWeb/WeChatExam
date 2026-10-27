using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.ContentChecksViewModels;

public enum CheckSeverity { Error, Warning, Information }

public enum ContentPricing { Unspecified, Public, Paid }

public record ContentReference(string Controller, string Id, string Name, string Permission,
    ContentPricing Pricing = ContentPricing.Unspecified, string Action = "Edit", bool? Enabled = null);
public record AccessObservation(string Scenario, string Entry, bool Allowed, string Reason);
public record AccessScenario(string Name, PaperAccessStatus Status);
public record KnowledgeContentAccess(ContentReference KnowledgePoint, bool Allowed);
public record ExerciseAccess(string Scenario, bool Allowed, List<KnowledgeContentAccess> Knowledge);
public record ContentTagMapping(ContentReference? Taxonomy, List<ContentReference> Subjects);

// Preserve actual database edges before findings are grouped. A group's flat object list
// cannot tell us which of its knowledge points belongs to which exercise.
public record ContentExercise(ContentReference Question, List<ContentReference> KnowledgePoints,
    ContentReference? Subject, ContentReference Period, List<ContentReference> Settings,
    List<ExerciseAccess> Access);

public class ContentFinding
{
    public required string Rule { get; init; }
    public required CheckSeverity Severity { get; init; }
    public bool NeedsEngineering { get; init; }
    public Guid? QuestionSubjectId { get; set; }
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
    public int KnowledgePointCount { get; set; }
    public int QuestionCount { get; set; }
    public int PaperCount { get; set; }
    public List<TotalCategory> Categories { get; set; } = [];
    public List<ContentFinding> Findings { get; } = [];
    public List<AccessScenario> Scenarios { get; } = [];
    public Dictionary<int, List<ContentReference>> TagImpacts { get; } = new();
    public Dictionary<Guid, List<ContentReference>> PeriodImpacts { get; } = new();
    public Dictionary<Guid, ContentExercise> Exercises { get; } = new();
    public Dictionary<int, ContentTagMapping> TagMappings { get; } = new();
}

/// <summary>
/// Groups only findings with the same configuration cause. The original diagnostics
/// remain intact, including every affected object and endpoint observation.
/// </summary>
public class ContentFindingGroup
{
    public required string Key { get; init; }
    public required List<ContentFinding> Findings { get; init; }
    public string Caption { get; set; } = string.Empty;
    public string StudentExperience { get; set; } = string.Empty;
    public CheckSeverity Severity => Findings[0].Severity;
    public bool NeedsEngineering => Findings[0].NeedsEngineering;
    public string Rule => Findings[0].Rule;
    public List<ContentReference> Objects => Findings.SelectMany(f => f.Objects).DistinctBy(o => (o.Controller, o.Id)).ToList();
    public IEnumerable<AccessObservation> Observations => Findings.SelectMany(f => f.Observations).Distinct();
    public IEnumerable<Guid> CategoryIds => Findings.SelectMany(f => f.CategoryIds).Distinct();
    public ContentReference? Primary => Rule.StartsWith("KP", StringComparison.Ordinal)
        ? Objects.FirstOrDefault(o => o.Controller == "KnowledgePoints") ?? Objects.FirstOrDefault()
        : Objects.FirstOrDefault();
    public string Icon => Primary?.Controller switch
    {
        "Tags" => "tag", "Taxonomies" => "folder-tree", "Questions" => "list-checks",
        "Papers" => "files", "KnowledgePoints" => "book-open", "VipProducts" => "badge-check",
        _ => "clipboard-check"
    };
    public string Tone => NeedsEngineering ? "secondary" : Severity switch
    {
        CheckSeverity.Error => "danger", CheckSeverity.Warning => "warning", _ => "secondary"
    };
    public string DetailsUrl { get; set; } = string.Empty;

    public static List<ContentFindingGroup> Build(IEnumerable<ContentFinding> findings) => findings
        .GroupBy(Fingerprint).Select(g => new ContentFindingGroup
        {
            Key = g.Key, Findings = g.OrderBy(f => f.Objects.FirstOrDefault()?.Id).ToList()
        }).ToList();

    private static string Fingerprint(ContentFinding finding)
    {
        // Questions sharing a paid period and course requirements describe one configuration
        // decision. Never combine different rules, subjects or engineering responsibilities.
        if (finding.Rule is "KP001" or "KP002" or "Q001" or "Q002" or "VIP002")
        {
            // Knowledge directories do not add question VIP requirements.
            // Their category context matters only for the cross-course association warning.
            var relatedSubjects = finding.Rule == "KP002" ? string.Join(',', finding.CategoryIds.Order()) : string.Empty;
            return finding.Rule + ":question-subject=" + (finding.QuestionSubjectId?.ToString() ?? "legacy") + ":periods=" + string.Join(',', finding.Objects.Where(o => o.Controller == "QuestionCategories").Select(o => o.Id).Order())
                + ":subjects=" + relatedSubjects;
        }
        var first = finding.Objects.FirstOrDefault();
        // Different empty historical paper versions must remain independently inspectable.
        var version = finding.Rule == "PAPER002" ? ":" + finding.Observations.FirstOrDefault()?.Entry : string.Empty;
        return finding.Rule + ":" + first?.Controller + ":" + first?.Id + version;
    }
}

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Content Configuration Checks";
    public ContentCheckReport? Report { get; set; }
    public List<ContentFindingGroup> Groups { get; set; } = [];
    public List<ContentFindingGroup> Results { get; set; } = [];
    public ContentFindingGroup? Selected { get; set; }
    public int Total { get; set; }
    public int Page { get; set; } = 1;
    public const int PageSize = 12;
    public string View { get; set; } = "all";
    public string? Severity { get; set; }
    public string? Rule { get; set; }
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid[] Subjects { get; set; } = [];
    public bool RealExam { get; set; }
    public bool Changed { get; set; }
    public List<ContentExercise> Exercises { get; set; } = [];
    public ContentExercise? Exercise { get; set; }
    public int ExerciseIndex { get; set; }
    public string? PreviousExerciseUrl { get; set; }
    public string? NextExerciseUrl { get; set; }
    public Dictionary<string, string> ExerciseUrls { get; } = new();
    public string DetailsUrl { get; set; } = string.Empty;
    public HashSet<string> EditPermissions { get; set; } = [];
    public string ResultsUrl { get; set; } = string.Empty;
    public string? PreviousUrl { get; set; }
    public string? NextUrl { get; set; }
    public Dictionary<string, string> ViewUrls { get; set; } = new();
}
