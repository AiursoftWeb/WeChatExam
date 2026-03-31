using System.ComponentModel.DataAnnotations;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

/// <summary>
/// 创建支付订单请求
/// </summary>
public class CreatePaymentOrderRequest
{
    [Required]
    public Guid VipProductId { get; set; }

    /// <summary>
    /// 优惠码（可选）
    /// </summary>
    public string? CouponCode { get; set; }
}

/// <summary>
/// 创建支付订单响应
/// </summary>
public class CreatePaymentOrderResponse
{
    public bool Success { get; set; }
    public string? OutTradeNo { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 小程序调起支付参数
    /// </summary>
    public PaymentJsApiParams? PayParams { get; set; }
}

/// <summary>
/// 小程序调起支付参数
/// </summary>
public class PaymentJsApiParams
{
    public string AppId { get; set; } = string.Empty;
    public string TimeStamp { get; set; } = string.Empty;
    public string NonceStr { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public string SignType { get; set; } = "RSA";
    public string PaySign { get; set; } = string.Empty;
}

/// <summary>
/// 订单状态响应
/// </summary>
public class PaymentOrderStatusResponse
{
    public string OutTradeNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AmountInFen { get; set; }
    public string? WechatTransactionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>
/// VIP 状态响应
/// </summary>
public class VipStatusResponse
{
    public List<VipMembershipDto> Memberships { get; set; } = [];
}

public class VipMembershipDto
{
    public VipProductType Type { get; set; }
    public Guid? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? EndTime { get; set; }
}
