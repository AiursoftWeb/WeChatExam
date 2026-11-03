using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.TotalCategoriesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Total Categories";
    }

    public List<ContentsViewModel> Categories { get; init; } = [];
}
