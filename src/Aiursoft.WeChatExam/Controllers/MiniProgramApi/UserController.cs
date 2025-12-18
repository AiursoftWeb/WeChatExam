using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly] // 仅允许微信小程序访客
public class UserController(UserManager<User> userManager) : ControllerBase
{
    [HttpGet("info")]
    public async Task<IActionResult> GetUserInfo()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            user.DisplayName,
            user.AvatarRelativePath,
            user.MiniProgramOpenId,
            user.CreationTime
        });
    }

    [HttpGet("home")]
    [AllowAnonymous]
    public IActionResult Index()
    {
        return Ok(new { Message = "Welcome to WeChat Mini Program Backend", Time = DateTime.UtcNow });
    }
    [HttpPost("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto model)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        user.DisplayName = model.NickName;
        user.AvatarRelativePath = model.AvatarUrl;
        await userManager.UpdateAsync(user);

        return Ok(new { Message = "Profile updated successfully" });
    }
}
