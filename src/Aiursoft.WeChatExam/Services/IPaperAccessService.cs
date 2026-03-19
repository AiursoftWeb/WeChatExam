using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 表示用户的试卷访问权限状态（缓存的 VIP 信息）
/// </summary>
public class PaperAccessStatus
{
    public HashSet<Guid> ActiveCategoryVips { get; set; } = new();
    public bool HasRealExamVip { get; set; }
}

public interface IPaperAccessService
{
    /// <summary>
    /// 获取用户当前的试卷访问状态（包含各类型 VIP 权限）
    /// </summary>
    Task<PaperAccessStatus> GetUserAccessStatusAsync(string? userId);

    /// <summary>
    /// 检查是否有权限访问特定的试卷
    /// </summary>
    bool HasAccess(Paper paper, PaperAccessStatus accessStatus);
}
