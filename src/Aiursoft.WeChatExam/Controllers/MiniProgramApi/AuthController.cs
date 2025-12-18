using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
public class AuthController(
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<AppSettings> appSettings,
    ILogger<AuthController> logger,
    IWeChatService weChatService) : ControllerBase
{
    private readonly AppSettings _appSettings = appSettings.Value;

    /// <summary>
    /// WeChat mini-program login endpoint
    /// Exchanges WeChat code for JWT token, following the template pattern
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] Code2SessionDto model)
    {
        if (!_appSettings.WeChatEnabled)
        {
            return BadRequest("WeChat authentication is not enabled");
        }

        if (string.IsNullOrWhiteSpace(model.Code))
        {
            return BadRequest("Code is empty");
        }

        // 1. Exchange code for session (similar to OIDC token exchange)
        var sessionResult = await weChatService.CodeToSessionAsync(model.Code);
        if (!sessionResult.IsSuccess)
        {
            logger.LogError("WeChat login failed: ErrorCode={ErrorCode}, Message={Message}",
                sessionResult.ErrorCode, sessionResult.ErrorMessage);
            return BadRequest($"WeChat login failed: {sessionResult.ErrorMessage}");
        }

        var openId = sessionResult.OpenId!;
        var sessionKey = sessionResult.SessionKey!;

        // 2. Sync user to local database (similar to SyncOidcContext in template)
        var user = await SyncWeChatUser(openId, sessionKey);
        if (user == null)
        {
            return StatusCode(500, "Failed to create or sync user");
        }

        // 3. Generate JWT token for API access
        var tokenString = WeChatTokenHelper.GenerateJwtToken(
            user,
            openId,
            _appSettings.WeChat.AppSecret,
            expirationDays: 7);

        return Ok(new TokenDto
        {
            Token = tokenString,
            Expiration = DateTime.UtcNow.AddDays(7),
            OpenId = openId
        });
    }

    /// <summary>
    /// Debug endpoint to exchange a magic key for a WeChat token
    /// For development/testing purposes only - allows getting a WeChat token without actual WeChat login
    /// </summary>
    [HttpPost("exchange_debug_token")]
    public async Task<IActionResult> ExchangeDebugToken([FromBody] DebugTokenRequestDto model)
    {
        // Check if debug magic key is configured
        if (string.IsNullOrWhiteSpace(_appSettings.DebugMagicKey))
        {
            logger.LogWarning("Debug token exchange attempted but DebugMagicKey is not configured");
            return BadRequest("Debug token exchange is not available");
        }

        // Validate the magic key
        if (model.MagicKey != _appSettings.DebugMagicKey)
        {
            logger.LogWarning("Invalid magic key provided for debug token exchange");
            return Unauthorized("Invalid magic key");
        }

        logger.LogInformation("Debug token exchange requested with valid magic key");

        // Find or create the debugger user
        var debugUser = userManager.Users.FirstOrDefault(u => u.UserName == "debugger");
        if (debugUser == null)
        {
            debugUser = new User
            {
                UserName = "debugger",
                DisplayName = "Debug User",
                Email = "debugger@debug.local",
                MiniProgramOpenId = "debug_openid_" + Guid.NewGuid().ToString("N"),
                SessionKey = "debug_session_key",
                AvatarRelativePath = Entities.User.DefaultAvatarPath
            };

            logger.LogInformation("Creating new debugger user");
            var createResult = await userManager.CreateAsync(debugUser);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to create debugger user: {Errors}", errors);
                return StatusCode(500, "Failed to create debug user");
            }
        }

        // Generate JWT token for the debugger user
        var tokenString = WeChatTokenHelper.GenerateJwtToken(
            debugUser,
            debugUser.MiniProgramOpenId!,
            _appSettings.WeChat.AppSecret,
            expirationDays: 7);

        logger.LogInformation("Debug token generated for user 'debugger'");

        return Ok(new TokenDto
        {
            Token = tokenString,
            Expiration = DateTime.UtcNow.AddDays(7),
            OpenId = debugUser.MiniProgramOpenId!
        });
    }

    /// <summary>
    /// Sync WeChat user to local database, following the same pattern as SyncOidcContext in template
    /// </summary>
    private async Task<User?> SyncWeChatUser(string openId, string sessionKey)
    {
        logger.LogInformation(
            "Try to find the user in the local database with WeChat OpenId: '{OpenId}'",
            openId);

        // 1. Try to find the user by MiniProgramOpenId
        var localUser = userManager.Users.FirstOrDefault(u => u.MiniProgramOpenId == openId);

        // 2. If the user doesn't exist, create a new one
        if (localUser == null)
        {
            localUser = new User
            {
                UserName = "wx_" + Guid.NewGuid().ToString("N"),
                DisplayName = "WeChat User",
                Email = $"wx_{openId}@wechat.mini",
                MiniProgramOpenId = openId,
                SessionKey = sessionKey,
                AvatarRelativePath = Entities.User.DefaultAvatarPath
            };

            logger.LogInformation(
                "The user with WeChat OpenId '{OpenId}' doesn't exist in the local database. Create a new one.",
                openId);

            var createUserResult = await userManager.CreateAsync(localUser);
            if (!createUserResult.Succeeded)
            {
                var errors = string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to create a local user: {Errors}", errors);
                return null;
            }
        }
        else
        {
            // 3. Update session key and other information if user exists
            var updated = false;

            if (localUser.SessionKey != sessionKey)
            {
                logger.LogInformation("Updating session key for user '{Username}'", localUser.UserName);
                localUser.SessionKey = sessionKey;
                updated = true;
            }

            if (updated)
            {
                await userManager.UpdateAsync(localUser);
            }
        }

        // 4. Add the default role if configured (similar to OIDC flow)
        if (!string.IsNullOrWhiteSpace(_appSettings.DefaultRole))
        {
            var userRoles = await userManager.GetRolesAsync(localUser);
            if (!userRoles.Contains(_appSettings.DefaultRole))
            {
                if (!await roleManager.RoleExistsAsync(_appSettings.DefaultRole))
                {
                    logger.LogInformation("The role '{Role}' doesn't exist. Create a new one.", _appSettings.DefaultRole);
                    await roleManager.CreateAsync(new IdentityRole(_appSettings.DefaultRole));
                }

                logger.LogInformation("Add the default role '{Role}' to the user.", _appSettings.DefaultRole);
                await userManager.AddToRoleAsync(localUser, _appSettings.DefaultRole);
            }
        }

        return localUser;
    }
}
