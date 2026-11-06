using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Distribution Channel";
    }

    public required DistributionChannel Channel { get; set; }
    public CouponDeletionSummary Deletion { get; init; } = new();
    public int RegistrationCount { get; set; }
}
