using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>Membership and catalog reads do not require a payment gateway.</summary>
[ApiController]
[Route("api/payment")]
public class VipController(IVipMembershipService membershipService, IVipProductService vipProductService) : ControllerBase
{
    /// <summary>
    /// 获取用户的 VIP 状态列表
    /// </summary>
    /// <returns>包含各个 VIP 状态的响应</returns>
    [HttpGet("vip-status")]
    [ProducesResponseType(typeof(VipStatusResponse), StatusCodes.Status200OK)]
    [WeChatUserOnly]
    public async Task<IActionResult> GetVipStatus()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var vips = await membershipService.GetVipStatusListAsync(userId ?? string.Empty);

        var dtos = vips.Select(v => new VipMembershipDto
        {
            Type = v.VipProduct?.Type ?? VipProductType.Category,
            TotalCategoryId = v.VipProduct?.TotalCategoryId,
            TotalCategoryName = v.VipProduct?.TotalCategory?.Title ?? string.Empty,
            InstitutionId = v.VipProduct?.InstitutionId,
            InstitutionName = v.VipProduct?.Institution?.Title ?? string.Empty,
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
    public async Task<IActionResult> GetVipProducts(
        [FromQuery] Guid? totalCategoryId = null,
        [FromQuery] VipProductType? type = null,
        [FromQuery] Guid? institutionId = null)
    {
        // Course targets are total categories, never knowledge or question categories.
        // Reject obsolete filters rather than silently returning an unfiltered catalog.
        if (Request.Query.ContainsKey("categoryId"))
            return BadRequest(new { Message = "Specify totalCategoryId." });

        var products = await vipProductService.GetEnabledAsync(totalCategoryId, type, institutionId);
        
        var dtos = products.Select(p => new
        {
            p.Id,
            p.Name,
            Type = p.Type.ToString(),
            p.TotalCategoryId,
            TotalCategoryName = p.TotalCategory?.Title,
            p.InstitutionId,
            InstitutionName = p.Institution?.Title,
            p.PriceInFen,
            p.DurationDays
        }).ToList();
        
        return Ok(dtos);
    }
}
