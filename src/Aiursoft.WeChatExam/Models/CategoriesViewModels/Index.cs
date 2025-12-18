using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.CategoriesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Categories";
    }

    public List<Category> Categories { get; set; } = new();

    public List<Category> RootCategories { get; set; } = new();
}
