using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

public class CreateVirtualPaymentOrderRequest
{
    [Required]
    public Guid VipProductId { get; set; }

    // A fresh wx.login code, never an OpenId, session_key or client price.
    [Required, StringLength(512)]
    public string LoginCode { get; set; } = string.Empty;

    [StringLength(256)]
    public string? CouponCode { get; set; }
}

public class CreateVirtualPaymentOrderResponse
{
    public bool Success => true;
    public required string OutTradeNo { get; init; }
    public required string Status { get; init; }
    public int OriginalAmountInFen { get; init; }
    public int DiscountInFen { get; init; }
    public int AmountInFen { get; init; }
    public string? AppliedCouponCode { get; init; }
    public VirtualPaymentParameters? PayParams { get; init; }
}
