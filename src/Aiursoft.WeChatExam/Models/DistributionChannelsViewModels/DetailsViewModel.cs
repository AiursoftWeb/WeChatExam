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

    public string? ReturnUrl { get; set; }
    public required DistributionChannel Channel { get; set; }
    public required ChannelStats Stats { get; set; }
    public Dictionary<Guid, int> CouponPaidOrderCounts { get; init; } = [];
}
