using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aiursoft.WeChatExam.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Aiursoft.WeChatExam.Services.Authentication;

/// <summary>
/// Helper class for generating JWT tokens for WeChat mini-program API access
/// </summary>
public static class WeChatTokenHelper
{
    public static string GenerateJwtToken(User user, string openId, string appSecret, int expirationDays = 7)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(appSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                new Claim("wechat_openid", openId),
                new Claim("display_name", user.DisplayName)
            }),
            Expires = DateTime.UtcNow.AddDays(expirationDays),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
