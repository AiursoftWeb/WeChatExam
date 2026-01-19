using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
public class UserPracticeHistoryController : ControllerBase
{
    private readonly WeChatExamDbContext _context;
    private readonly IGradingService _gradingService;

    public UserPracticeHistoryController(
        WeChatExamDbContext context,
        IGradingService gradingService)
    {
        _context = context;
        _gradingService = gradingService;
    }

    /// <summary>
    /// 查询当前用户的刷题历史（可选按题目过滤）
    /// </summary>
    /// <param name="questionId">题目ID（可选，若提供则只返回该题目的历史）</param>
    /// <returns>刷题历史列表，按时间倒序排列</returns>
    /// <response code="200">成功返回历史记录列表</response>
    /// <response code="401">用户未登录</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<UserPracticeHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get([FromQuery] Guid? questionId)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var query = _context.UserPracticeHistories
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Question)
            .Where(x => x.UserId == userId);
        if (questionId.HasValue)
        {
            query = query.Where(x => x.QuestionId == questionId.Value);
        }
        var result = await query
            .OrderByDescending(x => x.CreationTime)
            .Select(x => new UserPracticeHistoryDto
            {
                Id = x.Id,
                QuestionId = x.QuestionId,
                UserAnswer = x.UserAnswer,
                StandardAnswer = x.Question.StandardAnswer,
                IsCorrect = x.IsCorrect,
                CreationTime = x.CreationTime
            })
            .ToListAsync();
        return Ok(result);
    }

    /// <summary>
    /// 新增刷题历史记录（用户ID自动从token获取）
    /// </summary>
    /// <param name="dto">刷题历史提交数据</param>
    /// <returns>创建成功的历史记录</returns>
    /// <response code="201">创建成功</response>
    /// <response code="400">用户或题目不存在</response>
    /// <response code="401">用户未登录</response>
    [HttpPost]
    [ProducesResponseType(typeof(CreateUserPracticeHistoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Post([FromBody] CreateUserPracticeHistoryDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // 检查用户和题目是否存在
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        var question = await _context.Questions.FirstOrDefaultAsync(q => q.Id == dto.QuestionId);
        
        if (!userExists || question == null)
        {
            return BadRequest(new { Message = "User or Question not found" });
        }

        // Server-side Grading
        var gradingResult = await _gradingService.GradeAsync(question, dto.UserAnswer);

        var entity = new UserPracticeHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = dto.QuestionId,
            UserAnswer = dto.UserAnswer,
            IsCorrect = gradingResult.IsCorrect,
            CreationTime = DateTime.UtcNow
        };
        _context.UserPracticeHistories.Add(entity);
        await _context.SaveChangesAsync();
        
        var result = new UserPracticeHistoryDto
        {
            Id = entity.Id,
            QuestionId = entity.QuestionId,
            UserAnswer = entity.UserAnswer,
            StandardAnswer = question.StandardAnswer,
            IsCorrect = entity.IsCorrect,
            CreationTime = entity.CreationTime
        };
        return CreatedAtAction(nameof(Get), new { questionId = dto.QuestionId }, result);
    }
}
