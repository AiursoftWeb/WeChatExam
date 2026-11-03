namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// VIP 商品类型
/// </summary>
public enum VipProductType
{
    /// <summary>
    /// 分类 VIP（关联具体分类，购买后可访问该分类下的付费试卷）
    /// </summary>
    Category = 0,

    /// <summary>
    /// Reserved historical database value. New products and entitlements must not use it.
    /// </summary>
    RealExam = 1,

    /// <summary>
    /// Real-paper access for one institution; never grants independent course access.
    /// Keep this numeric value unchanged while historical type 1 records remain stored.
    /// </summary>
    Institution = 2
}
