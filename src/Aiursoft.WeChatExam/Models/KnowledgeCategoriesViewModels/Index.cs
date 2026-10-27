using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Knowledge Categories";
    }

    public Guid? TotalCategoryId { get; set; }
    public List<TotalCategory> TotalCategories { get; set; } = new();

    public List<KnowledgeCategory> Categories { get; set; } = new();

    public List<KnowledgeCategory> RootCategories { get; set; } = new();
}
