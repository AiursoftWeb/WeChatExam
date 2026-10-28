using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MockPapersViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Mock Papers";

    public string? Search { get; init; }
    public Guid? TotalCategoryId { get; init; }
    public int Page { get; init; }
    public int Total { get; init; }
    public const int PageSize = 24;
    public List<MockPaper> Papers { get; init; } = [];
    public List<TotalCategory> Courses { get; init; } = [];
}
