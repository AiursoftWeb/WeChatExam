using System.Security.Claims;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/BlindSpotPreview")]
[ApiController]
[WeChatUserOnly]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class BlindSpotPreviewController(BlindSpotPreviewService preview, IPaperAccessService accessService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();
        var access = await accessService.GetUserAccessStatusAsync(userId);
        return Ok(await preview.FindAsync(userId, access, cancellationToken));
    }
}
