using System.Security.Claims;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// Separate virtual-payment contract. Never falls back to ordinary JSAPI.
/// Client success/cancellation is not proof of payment or gateway closure.
/// </summary>
[ApiController]
[Route("api/virtual-payment")]
[WeChatUserOnly]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class VirtualPaymentController(VirtualPaymentCheckoutService checkout,
    VirtualPaymentReconciliationService reconciliation, IOptions<AppSettings> options,
    ILogger<VirtualPaymentController> logger) : ControllerBase
{
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateVirtualPaymentOrderRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (!options.Value.WeChatEnabled || !options.Value.WeChat.VirtualPayment.Enabled)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { code = "virtual_payment_disabled", error = "Virtual payment is not enabled." });
        try
        {
            var result = await checkout.CreateAsync(userId, request.VipProductId, request.LoginCode,
                request.CouponCode, HttpContext.RequestAborted);
            return Ok(new CreateVirtualPaymentOrderResponse
            {
                OutTradeNo = result.OutTradeNo, Status = result.Status.ToString(),
                OriginalAmountInFen = checked(result.AmountInFen + result.DiscountInFen),
                DiscountInFen = result.DiscountInFen, AmountInFen = result.AmountInFen,
                AppliedCouponCode = result.AppliedCouponCode, PayParams = result.PayParams
            });
        }
        catch (VirtualPaymentCheckoutException ex)
        {
            // Only deliberate business rejections may become public messages.
            return BadRequest(new { code = "virtual_payment_rejected", error = ex.Message });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // SQL/SDK exceptions can include secrets. Preserve the reservation
            // and ask the caller to query; never expose or log the raw exception.
            logger.LogError("Virtual payment checkout could not be confirmed.");
            return Unavailable();
        }
    }

    [HttpGet("order-status/{outTradeNo}")]
    public async Task<IActionResult> GetOrderStatus(string outTradeNo)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        if (outTradeNo.Length > 32) return NotFound();
        try
        {
            // Recovery remains available after disabling NEW sales. This service
            // authorizes owner/channel before any gateway call or financial effect.
            var order = await reconciliation.ReconcileAsync(outTradeNo, userId, HttpContext.RequestAborted);
            if (order == null) return NotFound();
            return Ok(new PaymentOrderStatusResponse
            {
                OutTradeNo = order.OutTradeNo, Status = order.Status.ToString(), AmountInFen = order.AmountInFen,
                WechatTransactionId = order.WechatTransactionId, CreatedAt = order.CreatedAt, PaidAt = order.PaidAt
            });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogError("Virtual payment status could not be confirmed.");
            return Unavailable();
        }
    }

    private ObjectResult Unavailable() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { code = "virtual_payment_unavailable", error = "Payment could not be confirmed. Please check the order status before purchasing again." });
}
