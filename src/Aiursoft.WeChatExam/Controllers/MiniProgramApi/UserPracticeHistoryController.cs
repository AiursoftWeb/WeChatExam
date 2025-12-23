using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
public class UserPracticeHistoryController : ControllerBase
{
    private readonly TemplateDbContext _context;

    public UserPracticeHistoryController(TemplateDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 查询当前用户的刷题历史（可选按题目过滤）
    /// </summary>
    /// <param name="questionId">题目ID（可选）</param>
    /// <returns>刷题历史列表，按时间倒序</returns>
    [HttpGet]
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
                IsCorrect = x.IsCorrect,
                CreationTime = x.CreationTime
            })
            .ToListAsync();
        return Ok(result);
    }

    /// <summary>
    /// 新增刷题历史记录（用户ID自动从token获取）
    /// </summary>
    /// <param name="dto">刷题历史 DTO</param>
    /// <returns>创建结果</returns>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] CreateUserPracticeHistoryDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // 检查用户和题目是否存在
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        var questionExists = await _context.Questions.AnyAsync(q => q.Id == dto.QuestionId);
        if (!userExists || !questionExists)
        {
            return BadRequest(new { Message = "User or Question not found" });
        }
        var entity = new UserPracticeHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            QuestionId = dto.QuestionId,
            UserAnswer = dto.UserAnswer,
            IsCorrect = dto.IsCorrect,
            CreationTime = DateTime.UtcNow
        };
        _context.UserPracticeHistories.Add(entity);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { questionId = dto.QuestionId }, dto);
    }
}
