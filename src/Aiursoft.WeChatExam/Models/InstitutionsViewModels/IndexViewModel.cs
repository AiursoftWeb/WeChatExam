using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.InstitutionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Institutions";
    }

    public string? Search { get; init; }
    public int Page { get; init; }
    public int Total { get; init; }
    public const int PageSize = 24;

    public List<Institution> Institutions { get; init; } = [];
}
