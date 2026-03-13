using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 支付订单管理服务接口
/// </summary>
public interface IPaymentOrderService
{
    /// <summary>
    /// 获取所有订单（支持筛选）
    /// </summary>
    Task<List<PaymentOrder>> GetAllOrdersAsync(int page = 1, int pageSize = 50, PaymentOrderStatus? statusFilter = null, string? userIdFilter = null);

    /// <summary>
    /// 获取订单详情（含日志）
    /// </summary>
    Task<PaymentOrder?> GetOrderDetailAsync(Guid id);

    /// <summary>
    /// 获取某用户的所有支付记录
    /// </summary>
    Task<List<PaymentOrder>> GetUserPaymentsAsync(string userId);

    /// <summary>
    /// 获取订单总数
    /// </summary>
    Task<int> GetOrderCountAsync(PaymentOrderStatus? statusFilter = null, string? userIdFilter = null);
}
