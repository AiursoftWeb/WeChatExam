using System.Security.Claims;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 微信支付相关 API（小程序端调用）
/// </summary>
[ApiController]
[Route("api/payment")]
public class PaymentController(
UserManager<User> userManager,
    IWeChatPayService payService,
    IVipProductService vipProductService,
    ILogger<PaymentController> logger) : ControllerBase
{
    /// <summary>
    /// 创建支付订单（统一下单），返回小程序调起支付参数
    /// </summary>
    /// <param name="request">订单创建请求</param>
    /// <returns>支付参数或错误信息</returns>
    /// <response code="200">订单创建成功，返回支付参数</response>
    /// <response code="400">请求参数无效或业务逻辑限制</response>
    /// <response code="401">未认证</response>
    [HttpPost("create-order")]
    [WeChatUserOnly]
    public async Task<IActionResult> CreateOrder([FromBody] CreatePaymentOrderRequest request)
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

        var openId = user.MiniProgramOpenId;
        if  (string.IsNullOrEmpty(openId))
        {
            return Unauthorized(new { error = "用户身份信息不完整" });
        }

        var result = await payService.CreateOrderAsync(
            userId,
            openId,
            request.VipProductId);

        if (!result.Success)
        {
            return BadRequest(new { error = result.ErrorMessage });
        }

        return Ok(new CreatePaymentOrderResponse
        {
            Success = true,
            OutTradeNo = result.OutTradeNo,
            PayParams = result.PayParams == null ? null : new PaymentJsApiParams
            {
                AppId = result.PayParams.AppId,
                TimeStamp = result.PayParams.TimeStamp,
                NonceStr = result.PayParams.NonceStr,
                Package = result.PayParams.Package,
                SignType = result.PayParams.SignType,
                PaySign = result.PayParams.PaySign
            }
        });
    }

    /// <summary>
    /// 查询订单状态
    /// </summary>
    /// <param name="outTradeNo">商户订单号</param>
    /// <returns>订单状态</returns>
    /// <response code="200">查询成功</response>
    /// <response code="404">订单不存在</response>
    [HttpGet("order-status/{outTradeNo}")]
    [WeChatUserOnly]
    public async Task<IActionResult> GetOrderStatus(string outTradeNo)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var order = await payService.QueryOrderStatusAsync(outTradeNo);

        if (order == null || order.UserId != userId)
        {
            return NotFound(new { error = "订单不存在" });
        }

        return Ok(new PaymentOrderStatusResponse
        {
            OutTradeNo = order.OutTradeNo,
            Status = order.Status.ToString(),
            AmountInFen = order.AmountInFen,
            WechatTransactionId = order.WechatTransactionId,
            CreatedAt = order.CreatedAt,
            PaidAt = order.PaidAt
        });
    }

    /// <summary>
    /// 微信支付结果通知回调
    /// </summary>
    /// <response code="200">处理成功</response>
    /// <response code="500">处理失败</response>
    [HttpPost("notify")]
    [AllowAnonymous]
    public async Task<IActionResult> PaymentNotify()
    {
        using var reader = new StreamReader(Request.Body);
        var requestBody = await reader.ReadToEndAsync();

        var signature = Request.Headers["Wechatpay-Signature"].FirstOrDefault() ?? string.Empty;
        var timestamp = Request.Headers["Wechatpay-Timestamp"].FirstOrDefault() ?? string.Empty;
        var nonce = Request.Headers["Wechatpay-Nonce"].FirstOrDefault() ?? string.Empty;
        var serialNumber = Request.Headers["Wechatpay-Serial"].FirstOrDefault() ?? string.Empty;

        logger.LogInformation("Received payment notify, serial: {Serial}", serialNumber);

        var success = await payService.HandlePaymentNotifyAsync(
            requestBody, signature, timestamp, nonce, serialNumber);

        if (success)
        {
            // WeChat expects this exact JSON response for success
            return Ok(new { code = "SUCCESS", message = "OK" });
        }

        return StatusCode(500, new { code = "FAIL", message = "处理失败" });
    }

    /// <summary>
    /// 获取用户的 VIP 状态列表
    /// </summary>
    /// <returns>包含各个分类 VIP 状态的响应</returns>
    [HttpGet("vip-status")]
    [ProducesResponseType(typeof(VipStatusResponse), StatusCodes.Status200OK)]
    [WeChatUserOnly]
    public async Task<IActionResult> GetVipStatus()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var vips = await payService.GetVipStatusListAsync(userId ?? string.Empty);

        var dtos = vips.Select(v => new VipMembershipDto
        {
            CategoryId = v.VipProduct?.CategoryId ?? Guid.Empty,
            CategoryName = v.VipProduct?.Category?.Title ?? "Unknown",
            ProductName = v.VipProduct?.Name ?? "VIP",
            IsActive = v.IsActive,
            EndTime = v.EndTime
        }).ToList();

        return Ok(new VipStatusResponse
        {
            Memberships = dtos
        });
    }

    /// <summary>
    /// 获取可购买的 VIP 商品列表
    /// </summary>
    /// <returns>启用的 VIP 商品列表</returns>
    [HttpGet("vip-products")]
    public async Task<IActionResult> GetVipProducts()
    {
        var products = await vipProductService.GetEnabledAsync();
        
        var dtos = products.Select(p => new
        {
            p.Id,
            p.Name,
            p.CategoryId,
            CategoryName = p.Category?.Title,
            p.PriceInFen,
            p.DurationDays
        }).ToList();
        
        return Ok(dtos);
    }
}
