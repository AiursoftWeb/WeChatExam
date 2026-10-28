using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.RealPapersViewModels;

public class QuestionsViewModel : UiStackLayoutViewModel
{
    public QuestionsViewModel() => PageTitle = "Real Paper Questions";
    public required RealPaper Paper { get; init; }
    public bool Selecting { get; init; }
    public string? Search { get; init; }
    public Guid? QuestionCategoryId { get; init; }
    public int Page { get; init; }
    public int Total { get; init; }
    public int SelectedCount { get; init; }
    public long TotalScore { get; init; }
    public const int PageSize = 20;
    public List<QuestionCategory> Periods { get; init; } = [];
    public List<RealPaperQuestion> Selected { get; init; } = [];
    public List<Question> Available { get; init; } = [];
}
