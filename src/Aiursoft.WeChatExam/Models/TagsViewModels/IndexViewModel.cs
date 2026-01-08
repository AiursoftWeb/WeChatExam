using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TagsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public List<Tag> Tags { get; set; } = [];
    public Dictionary<int, int> TagUsageCounts { get; set; } = [];
    public string? SearchQuery { get; set; }
}
