namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 支付订单状态
/// </summary>
public enum PaymentOrderStatus
{
    /// <summary>
    /// 待支付
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已支付
    /// </summary>
    Paid = 1,

    /// <summary>
    /// 支付失败
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 已关闭
    /// </summary>
    Closed = 3,

    /// <summary>
    /// 已退款（预留）
    /// </summary>
    Refunded = 4
}
