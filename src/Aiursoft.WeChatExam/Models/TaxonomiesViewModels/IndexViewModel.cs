using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TaxonomiesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Taxonomies Management";
    }
    public List<Taxonomy> Taxonomies { get; set; } = new();
    public Dictionary<int, int> TagCounts { get; set; } = new();
}
