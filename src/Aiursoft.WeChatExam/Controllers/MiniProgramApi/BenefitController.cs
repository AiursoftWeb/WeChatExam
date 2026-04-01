using System.Security.Claims;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 优惠权益相关 API（小程序端调用）
/// </summary>
[ApiController]
[Route("api/benefit")]
public class BenefitController(
    ICouponService couponService,
    ILogger<BenefitController> logger) : ControllerBase
{
    /// <summary>
    /// 通过优惠码/分销码领取权益
    /// </summary>
    /// <param name="code">优惠码内容</param>
    /// <returns>领取结果</returns>
    [HttpPost("claim")]
    [WeChatUserOnly]
    public async Task<IActionResult> Claim([FromQuery] string code)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { error = "优惠码不能为空" });
        }

        var (success, errorMessage) = await couponService.ClaimCouponAsync(userId, code);
        if (!success)
        {
            logger.LogWarning("User {UserId} failed to claim coupon {Code}: {ErrorMessage}", 
                userId, code, errorMessage);
            return BadRequest(new { error = errorMessage });
        }

        logger.LogInformation("User {UserId} successfully claimed coupon {Code}", userId, code);
        return Ok(new { success = true, message = "权益领取成功" });
    }

    /// <summary>
    /// 获取当前用户已领取的所有可用权益
    /// </summary>
    /// <returns>可用优惠券列表</returns>
    [HttpGet("my")]
    [WeChatUserOnly]
    public async Task<IActionResult> MyBenefits()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var claimedCoupons = await couponService.GetMyAvailableCouponsAsync(userId);
        
        var dtos = claimedCoupons.Select(c => new
        {
            c.Code,
            c.AmountInFen,
            c.IsSingleUse,
            TargetProducts = c.TargetVipProducts.Select(tp => tp.VipProductId).ToList()
        });

        return Ok(dtos);
    }
}
