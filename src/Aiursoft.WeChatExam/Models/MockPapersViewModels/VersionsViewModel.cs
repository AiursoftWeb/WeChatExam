using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MockPapersViewModels;

public class VersionsViewModel : UiStackLayoutViewModel
{
    public VersionsViewModel() => PageTitle = "Mock Paper Versions";
    public required MockPaper Paper { get; init; }
    public List<PaperSnapshot> Versions { get; init; } = [];
    public PaperSnapshot? Snapshot { get; init; }
    public List<QuestionSnapshot> Questions { get; init; } = [];
    public long TotalScore { get; init; }
    public int Page { get; init; }
    public int Total { get; init; }
    public const int PageSize = 20;
    public int DraftQuestionCount { get; init; }
}
