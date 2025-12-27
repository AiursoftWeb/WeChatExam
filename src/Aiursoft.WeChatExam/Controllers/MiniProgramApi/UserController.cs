using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 用户信息管理 API
/// 仅允许微信小程序用户（Bearer token）访问
/// </summary>
[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]  // 明确要求 Bearer JWT 认证
public class UserController(UserManager<User> userManager, StorageService storageService) : ControllerBase
{
    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    /// <returns>包含用户昵称、头像、OpenId等信息的对象</returns>
    /// <response code="200">成功返回用户信息</response>
    /// <response code="401">未授权或Token无效</response>
    /// <response code="404">用户不存在</response>
    [HttpGet("info")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
        var avatarUrl = storageService.RelativePathToInternetUrl(user.AvatarRelativePath, HttpContext);

        return Ok(new
        {
            user.DisplayName,
            avatarUrl,
            user.MiniProgramOpenId,
            user.CreationTime
        });
    }

    /// <summary>
    /// API 连通性测试接口
    /// </summary>
    /// <returns>欢迎信息和服务器时间</returns>
    /// <response code="200">服务正常</response>
    [HttpGet("home")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Index()
    {
        return Ok(new { Message = "Welcome to WeChat Mini Program Backend", Time = DateTime.UtcNow });
    }

    /// <summary>
    /// 更新用户个人资料（昵称和头像）
    /// </summary>
    /// <param name="model">包含新昵称和头像路径的模型</param>
    /// <returns>操作结果消息</returns>
    /// <response code="200">更新成功</response>
    /// <response code="401">未授权</response>
    /// <response code="404">用户不存在</response>
    [HttpPost("profile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
