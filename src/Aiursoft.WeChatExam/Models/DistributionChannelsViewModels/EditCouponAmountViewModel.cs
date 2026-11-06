using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class EditCouponAmountViewModel : UiStackLayoutViewModel
{
    public EditCouponAmountViewModel() => PageTitle = "Edit Coupon Amount";

    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Discount Amount (CNY)")]
    [Range(typeof(decimal), "0", "21474836.47", ErrorMessage = "Enter an amount between 0 and 21474836.47 CNY.")]
    public decimal? Amount { get; set; }
}
