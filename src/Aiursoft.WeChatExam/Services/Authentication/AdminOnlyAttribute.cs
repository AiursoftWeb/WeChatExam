using Microsoft.AspNetCore.Authorization;

namespace Aiursoft.WeChatExam.Services.Authentication;

/// <summary>
/// 仅允许管理员访问 (通过网页Cookie认证)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminOnlyAttribute : AuthorizeAttribute
{
    public AdminOnlyAttribute()
    {
        Roles = "Admin";
    }
}
