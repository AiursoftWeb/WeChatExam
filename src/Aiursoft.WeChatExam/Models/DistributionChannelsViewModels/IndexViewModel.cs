using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Distribution Channels";
    }

    public List<DistributionChannel> Channels { get; set; } = [];
    public Dictionary<Guid, ChannelStats> ChannelStats { get; set; } = [];
    public string? SearchQuery { get; set; }
}
