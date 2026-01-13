using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Channel Details";
    }

    public required DistributionChannel Channel { get; set; }
    public required ChannelStats Stats { get; set; }
}
