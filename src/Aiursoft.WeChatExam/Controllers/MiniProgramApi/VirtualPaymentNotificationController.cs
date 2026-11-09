using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/virtual-payment")]
public class VirtualPaymentNotificationController(VirtualPaymentNotificationService notifications) : ControllerBase
{
    [HttpGet("notify")]
    public IActionResult Verify([FromQuery] string? timestamp, [FromQuery] string? nonce,
        [FromQuery] string? signature, [FromQuery] string? echostr)
    {
        if (!notifications.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        return !string.IsNullOrEmpty(echostr) && echostr.Length <= 4096 && notifications.VerifyEcho(timestamp, nonce, signature)
            ? Content(echostr, "text/plain") : StatusCode(StatusCodes.Status403Forbidden);
    }

    [HttpPost("notify")]
    [RequestSizeLimit(VirtualPaymentNotificationService.MaximumBodyBytes)]
    public async Task<IActionResult> Notify([FromQuery] string? timestamp, [FromQuery] string? nonce, [FromQuery(Name = "msg_signature")] string? signature)
    {
        if (!notifications.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        using var reader = new StreamReader(Request.Body);
        string body;
        try { body = await reader.ReadToEndAsync(HttpContext.RequestAborted); }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return StatusCode(StatusCodes.Status413PayloadTooLarge); }
        var result = await notifications.HandleAsync(body, timestamp, nonce, signature, HttpContext.RequestAborted);
        // WeChat explicitly accepts plain "success" for JSON and XML push. It
        // must only be sent after committed fulfillment, never on a parse/DB error.
        return result switch
        {
            VirtualPaymentNotificationResult.Completed => Content("success", "text/plain"),
            VirtualPaymentNotificationResult.Rejected => StatusCode(StatusCodes.Status403Forbidden),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable)
        };
    }
}
