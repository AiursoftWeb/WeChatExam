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
    [Display(Name = "Discount Amount (Fen)")]
    [Range(0, int.MaxValue, ErrorMessage = "Enter a whole number of fen between 0 and 2147483647.")]
    public int? AmountInFen { get; set; }
}
