using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.FileStorage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[ApiController]
[AllowAnonymous]
[Route("api/app-info")]
public class AppInfoController(IGlobalSettingsService settings, StorageService storage) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicAppInfoDto>> Get()
    {
        var logo = await settings.GetSettingValueAsync(SettingsMap.ProjectLogo);
        // Deliberately expose only the public presentation fields, never all settings.
        return new PublicAppInfoDto(
            string.IsNullOrWhiteSpace(logo) ? string.Empty : storage.RelativePathToInternetUrl(logo, HttpContext),
            (await settings.GetSettingValueAsync(SettingsMap.PublicOperatorName)).Trim(),
            (await settings.GetSettingValueAsync(SettingsMap.PublicTechnicalProvider)).Trim(),
            (await settings.GetSettingValueAsync(SettingsMap.PublicSupportContact)).Trim(),
            (await settings.GetSettingValueAsync(SettingsMap.PublicSupportPhone)).Trim(),
            (await settings.GetSettingValueAsync(SettingsMap.PublicSupportEmail)).Trim(),
            (await settings.GetSettingValueAsync(SettingsMap.PublicSupportWechat)).Trim());
    }
}
