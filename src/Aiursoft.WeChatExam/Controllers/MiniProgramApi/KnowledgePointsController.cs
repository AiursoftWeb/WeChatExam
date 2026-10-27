using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
public class KnowledgePointsController : ControllerBase
{
    private readonly WeChatExamDbContext _context;

    public KnowledgePointsController(WeChatExamDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取单个知识点的详细内容
    /// </summary>
    /// <param name="id">知识点ID</param>
    /// <returns>知识点详细信息</returns>
    /// <response code="200">成功返回知识点内容</response>
    /// <response code="404">未找到指定知识点</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKnowledgeContent(Guid id)
    {
        var knowledgePoint = await _context.KnowledgePoints
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null)
        {
            return NotFound(new { Message = "KnowledgePoint not found" });
        }

        var knowledgeDto = new KnowledgeDto
        {
            Id = knowledgePoint.Id,
            Title = knowledgePoint.Title,
            Content = knowledgePoint.Content,
            AudioUrl = knowledgePoint.AudioUrl,
        };

        return Ok(knowledgeDto);
    }

    /// <summary>
    /// 通过分类ID获取知识点及其相关题目
    /// </summary>
    /// <param name="knowledgeCategoryId">分类ID</param>
    /// <returns>知识点及题目列表</returns>
    /// <response code="200">成功返回知识点及题目列表</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<KnowledgePointWithQuestionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory([FromQuery] Guid knowledgeCategoryId)
    {
        // 查询所有属于该分类的知识点
        var knowledgePoints = await _context.KnowledgeCategoryKnowledgePoints
            .Where(x => x.KnowledgeCategoryId == knowledgeCategoryId)
            .Include(x => x.KnowledgePoint)
                .ThenInclude(kp => kp.KnowledgePointQuestions)
                    .ThenInclude(kpq => kpq.Question)
            .OrderBy(x => x.OrderIndex)
            .ThenBy(x => x.KnowledgePoint.CreationTime)
            .ThenBy(x => x.KnowledgePointId)
            .Select(x => x.KnowledgePoint)
            .ToListAsync();

        var result = knowledgePoints.Select(kp => new KnowledgePointWithQuestionsDto
        {
            Id = kp.Id,
            Title = kp.Title,
            Text = kp.Content,
            AudioUrl = kp.AudioUrl,
            Questions = kp.KnowledgePointQuestions
                .Select(kpq => kpq.Question)
                .Select(q => new QuestionContentDto
                {
                    Id = q.Id,
                    Text = q.Content,
                    // Deployed Mini Program clients trim selected option text before
                    // comparing locally. Match the grading service's whitespace handling.
                    Answer = q.StandardAnswer.Trim(),
                }).ToList()
        }).ToList();

        return Ok(result);
    }

}
