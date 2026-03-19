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
    /// 真题 VIP（独立于分类，购买后可访问所有真题试卷）
    /// </summary>
    RealExam = 1
}
