using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.RealPapersViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Real Papers";

    public string? Search { get; init; }
    public Guid? InstitutionId { get; init; }
    public int Page { get; init; }
    public int Total { get; init; }
    public const int PageSize = 24;
    public List<RealPaper> Papers { get; init; } = [];
    public List<Institution> Institutions { get; init; } = [];
}
