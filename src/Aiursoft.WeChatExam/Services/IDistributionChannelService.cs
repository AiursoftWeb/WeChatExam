using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 渠道统计数据
/// </summary>
public class ChannelStats
{
    /// <summary>
    /// 通过该渠道注册的用户数
    /// </summary>
    public int RegistrationCount { get; set; }

    /// <summary>
    /// 付费订单数（当前未实现，返回0）
    /// </summary>
    public int PaidOrderCount { get; set; }

    /// <summary>
    /// 付费总金额（当前未实现，返回0）
    /// </summary>
    public decimal TotalPaidAmount { get; set; }
}

public interface IDistributionChannelService
{
    /// <summary>
    /// 创建新的分销渠道，系统自动生成唯一码
    /// </summary>
    Task<DistributionChannel> CreateAsync(string agencyName);

    /// <summary>
    /// 获取所有分销渠道
    /// </summary>
    Task<List<DistributionChannel>> GetAllAsync();

    /// <summary>
    /// 根据ID获取渠道
    /// </summary>
    Task<DistributionChannel?> GetByIdAsync(Guid id);

    /// <summary>
    /// 根据分销码获取渠道
    /// </summary>
    Task<DistributionChannel?> GetByCodeAsync(string code);

    /// <summary>
    /// 设置渠道的启用/禁用状态
    /// </summary>
    Task SetEnabledAsync(Guid id, bool isEnabled);

    /// <summary>
    /// 将用户绑定到分销渠道
    /// 仅在用户未绑定任何渠道且渠道已启用时成功
    /// </summary>
    /// <returns>绑定是否成功</returns>
    Task<bool> BindUserAsync(string userId, string code);

    /// <summary>
    /// 获取渠道的统计数据
    /// </summary>
    Task<ChannelStats> GetStatsAsync(Guid channelId);
    
    /// <summary>
    /// 删除分销渠道
    /// </summary>
    Task DeleteAsync(Guid id);
}
