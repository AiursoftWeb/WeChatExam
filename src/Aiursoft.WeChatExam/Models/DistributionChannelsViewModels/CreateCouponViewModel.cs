using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class CreateCouponViewModel: UiStackLayoutViewModel
{
    [Required]
    public Guid ChannelId { get; set; }

    public string? ChannelName { get; set; }

    [Required]
    [MaxLength(32)]
    [Display(Name = "Coupon Code")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Discount Amount (Fen)")]
    public int AmountInFen { get; set; }

    [Display(Name = "Is Single Use")]
    public bool IsSingleUse { get; set; }

    [Display(Name = "Target Products (Optional)")]
    public List<Guid>? TargetVipProductIds { get; set; }

    public List<VipProduct> AvailableVipProducts { get; set; } = new();
}
