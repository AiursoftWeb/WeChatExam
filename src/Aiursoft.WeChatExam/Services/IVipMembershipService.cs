using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Reads saved memberships independently of any payment provider.</summary>
public interface IVipMembershipService
{
    Task<List<VipMembership>> GetVipStatusListAsync(string userId);
}
