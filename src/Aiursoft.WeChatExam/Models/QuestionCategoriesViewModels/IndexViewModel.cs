using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.QuestionCategoriesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Periods";
    }

    public List<TotalCategory> TotalCategories { get; init; } = [];
    public Guid? TotalCategoryId { get; init; }
    public List<QuestionCategory> Categories { get; init; } = [];
}
