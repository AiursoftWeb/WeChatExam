using System.Security.Claims;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 用户反馈 API
/// 仅允许微信小程序用户（Bearer token）访问
/// </summary>
[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
public class FeedbackController(IFeedbackService feedbackService) : ControllerBase
{
    /// <summary>
    /// 提交反馈接口
    /// </summary>
    /// <param name="model">反馈模型</param>
    /// <returns>操作结果</returns>
    /// <response code="200">提交成功</response>
    /// <response code="401">未授权</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackDto model)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        await feedbackService.SubmitFeedbackAsync(userId, model.Content, model.Contact);
        return Ok(new { Message = "Feedback submitted successfully" });
    }

    /// <summary>
    /// 获取我的反馈记录
    /// </summary>
    /// <returns>反馈记录列表</returns>
    /// <response code="200">成功返回反馈列表</response>
    /// <response code="401">未授权</response>
    [HttpGet("my")]
    [ProducesResponseType(typeof(List<FeedbackResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyFeedbacks()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var feedbacks = await feedbackService.GetUserFeedbacksAsync(userId);
        var response = feedbacks.Select(f => new FeedbackResponseDto
        {
            Id = f.Id,
            Content = f.Content,
            Contact = f.Contact,
            Status = f.Status,
            CreatedAt = f.CreatedAt
        }).ToList();

        return Ok(response);
    }
}
