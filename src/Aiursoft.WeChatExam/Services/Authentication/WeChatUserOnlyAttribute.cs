using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace Aiursoft.WeChatExam.Services.Authentication;

/// <summary>
/// Specifies that the action requires WeChat mini-program authentication via JWT Bearer token.
/// This explicitly uses Bearer authentication scheme, preventing Cookie-authenticated users from accessing.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class WeChatUserOnlyAttribute : AuthorizeAttribute
{
    public WeChatUserOnlyAttribute()
    {
        // Explicitly require Bearer authentication (JWT tokens)
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme;
    }
}
