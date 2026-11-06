using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class DeleteCouponViewModel : UiStackLayoutViewModel
{
    public DeleteCouponViewModel() => PageTitle = "Delete Coupon";
    public required Coupon Coupon { get; init; }
    public required CouponDeletionSummary Deletion { get; init; }
}
