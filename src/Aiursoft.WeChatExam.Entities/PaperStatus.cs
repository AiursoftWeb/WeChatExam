namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 试卷状态
/// </summary>
public enum PaperStatus
{
    /// <summary>
    /// 草稿状态，不能发布
    /// </summary>
    Draft = 0,

    /// <summary>
    /// 可发布状态，可用于发布生成快照
    /// </summary>
    Publishable = 1,

    /// <summary>
    /// 冻结状态，不可修改，已生成最终快照
    /// </summary>
    Frozen = 2
}
