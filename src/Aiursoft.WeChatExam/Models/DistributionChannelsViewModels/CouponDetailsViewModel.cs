using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class CouponDetailsViewModel : UiStackLayoutViewModel
{
    public CouponDetailsViewModel()
    {
        PageTitle = "Coupon Usage Details";
    }

    public required Coupon Coupon { get; init; }
    public required List<PaymentOrder> Orders { get; init; }
    public int TotalCount { get; init; }
    public long TotalDiscountInFen { get; init; }
    public long TotalPaidInFen { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Search { get; init; }
    public int CurrentPage { get; init; }
    public required string TimeZone { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / 25d));
}
