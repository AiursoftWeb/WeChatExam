using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// VIP 商品管理服务接口
/// </summary>
public interface IVipProductService
{
    /// <summary>
    /// 获取所有 VIP 商品（含分类信息）
    /// </summary>
    Task<List<VipProduct>> GetAllAsync();

    /// <summary>
    /// 获取单个 VIP 商品
    /// </summary>
    Task<VipProduct?> GetByIdAsync(Guid id);

    /// <summary>
    /// 创建 VIP 商品
    /// </summary>
    Task<VipProduct> CreateAsync(string name, Guid categoryId, int priceInFen, int durationDays);

    /// <summary>
    /// 更新 VIP 商品
    /// </summary>
    Task UpdateAsync(Guid id, string name, Guid categoryId, int priceInFen, int durationDays, bool isEnabled);

    /// <summary>
    /// 删除 VIP 商品
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// 获取所有启用的 VIP 商品（面向小程序端）
    /// </summary>
    Task<List<VipProduct>> GetEnabledAsync();
}
