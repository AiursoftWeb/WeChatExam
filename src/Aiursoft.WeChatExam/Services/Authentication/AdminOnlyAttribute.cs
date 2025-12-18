using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.WeChatExam.Services.Authentication;

/// <summary>
/// 仅允许管理员访问 (通过网页Cookie认证，并且必须拥有Admin角色)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminOnlyAttribute : AuthorizeAttribute
{
    public AdminOnlyAttribute()
    {
        // 明确指定使用 Cookie 认证方案 (本地登录或OIDC)
        // 这样可以防止使用 JWT Bearer token 的微信用户访问管理员功能
        AuthenticationSchemes = IdentityConstants.ApplicationScheme;
        Roles = "Admin";
    }
}
