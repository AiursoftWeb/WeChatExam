using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models;

public class PaperPublicationViewModel : UiStackLayoutViewModel
{
    public PaperPublicationViewModel() => PageTitle = "Paper Publication";
    public Guid Id { get; init; }
    public bool IsReal { get; init; }
    public string Controller => IsReal ? "RealPapers" : "MockPapers";
    public required string Title { get; init; }
    public PaperStatus Status { get; init; }
    public PaperSnapshot? Latest { get; init; }
    public required PaperSnapshot Current { get; init; }
    public bool Published => Status is PaperStatus.Publishable or PaperStatus.Frozen && Latest != null;
    public bool HasChanges => Latest == null || !Services.PaperPublication.SameContent(Current, Latest);
    public List<QuestionSnapshot> AnswerIssues => Current.QuestionSnapshots.Where(Services.PaperPublication.NeedsAnswer).ToList();
    public int MissingExplanations => Current.QuestionSnapshots.Count(q => string.IsNullOrWhiteSpace(q.Explanation));
}
