using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 微信支付服务接口
/// </summary>
public interface IWeChatPayService
{
    /// <summary>
    /// 创建统一下单（根据 VipProductId 获取价格和描述）
    /// </summary>
    Task<CreateOrderResult> CreateOrderAsync(string userId, string openId, Guid vipProductId);

    /// <summary>
    /// 生成小程序调起支付的参数
    /// </summary>
    Task<JsApiPayParams> GetJsApiPayParamsAsync(string prepayId);

    /// <summary>
    /// 处理支付结果通知回调
    /// </summary>
    Task<bool> HandlePaymentNotifyAsync(string requestBody, string signature, string timestamp, string nonce, string serialNumber);

    /// <summary>
    /// 查询订单状态
    /// </summary>
    Task<PaymentOrder?> QueryOrderStatusAsync(string outTradeNo);

    /// <summary>
    /// 查询用户所有 VIP 状态列表
    /// </summary>
    Task<List<VipMembership>> GetVipStatusListAsync(string userId);

    /// <summary>
    /// 检查用户是否拥有指定分类的有效 VIP
    /// </summary>
    Task<bool> HasVipForCategoryAsync(string userId, Guid categoryId);
}

/// <summary>
/// 创建订单结果
/// </summary>
public class CreateOrderResult
{
    public bool Success { get; set; }
    public string? OutTradeNo { get; set; }
    public string? PrepayId { get; set; }
    public string? ErrorMessage { get; set; }
    public JsApiPayParams? PayParams { get; set; }
}

/// <summary>
/// 小程序 JSAPI 调起支付参数
/// </summary>
public class JsApiPayParams
{
    public string AppId { get; set; } = string.Empty;
    public string TimeStamp { get; set; } = string.Empty;
    public string NonceStr { get; set; } = string.Empty;
    public string Package { get; set; } = string.Empty;
    public string SignType { get; set; } = "RSA";
    public string PaySign { get; set; } = string.Empty;
}
