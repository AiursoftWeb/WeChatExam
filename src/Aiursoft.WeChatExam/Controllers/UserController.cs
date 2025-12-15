using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
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
}
