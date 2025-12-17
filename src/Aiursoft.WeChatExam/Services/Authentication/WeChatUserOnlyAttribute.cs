using Microsoft.AspNetCore.Authorization;

namespace Aiursoft.WeChatExam.Services.Authentication;

/// <summary>
/// 仅允许微信小程序访客访问 (通过JWT Bearer认证)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class WeChatUserOnlyAttribute : AuthorizeAttribute
{
    public WeChatUserOnlyAttribute()
    {
        AuthenticationSchemes = "Bearer";
    }
}
