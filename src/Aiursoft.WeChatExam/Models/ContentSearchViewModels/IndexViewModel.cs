using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ContentSearchViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() => PageTitle = "Search Content";
    public string Query { get; set; } = string.Empty;
    public string Kind { get; set; } = "all";
    public Guid? CategoryId { get; set; }
    public List<Category> Categories { get; set; } = [];
    public List<ContentSearchItem> Results { get; set; } = [];
    public bool UsedAi { get; set; }
}
