using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.TagsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Tags";
    }
    public List<Tag> Tags { get; set; } = [];
    public List<Taxonomy> AllTaxonomies { get; set; } = [];
    public Dictionary<int, int> TagUsageCounts { get; set; } = [];
    public string? SearchQuery { get; set; }
    public int? TaxonomyId { get; set; }
    public IEnumerable<SelectListItem> AvailableTaxonomies { get; set; } = new List<SelectListItem>();
}
