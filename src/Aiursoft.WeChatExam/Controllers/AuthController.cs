using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController(
    UserManager<User> userManager,
    IOptions<AppSettings> appSettings,
    ILogger<AuthController> logger,
    IWeChatService weChatService) : ControllerBase
{
    private readonly AppSettings _appSettings = appSettings.Value;

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] Code2SessionDto model)
    {
        if (string.IsNullOrWhiteSpace(model.Code))
        {
            return BadRequest("Code is empty");
        }

        var sessionResult = await weChatService.CodeToSessionAsync(model.Code);
        if (!sessionResult.IsSuccess)
        {
            logger.LogError("WeChat login failed: ErrorCode={ErrorCode}, Message={Message}",
                sessionResult.ErrorCode, sessionResult.ErrorMessage);
            return BadRequest($"WeChat login failed: {sessionResult.ErrorMessage}");
        }

        var openId = sessionResult.OpenId!;
        var sessionKey = sessionResult.SessionKey!;

        var user = userManager.Users.FirstOrDefault(u => u.MiniProgramOpenId == openId);
        if (user == null)
        {
            user = new User
            {
                UserName = "wx_" + Guid.NewGuid().ToString("N"),
                DisplayName = "WeChat User",
                MiniProgramOpenId = openId,
                SessionKey = sessionKey,
                AvatarRelativePath = Entities.User.DefaultAvatarPath
            };
            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(result.Errors);
            }
        }
        else
        {
            user.SessionKey = sessionKey;
            await userManager.UpdateAsync(user);
        }

        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_appSettings.WechatAppSecret);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                new Claim("OpenId", openId)
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return Ok(new TokenDto
        {
            Token = tokenString,
            Expiration = tokenDescriptor.Expires.Value,
            OpenId = openId
        });
    }
}
