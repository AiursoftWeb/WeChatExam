using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
public class KnowledgePointsController : ControllerBase
{
    private readonly TemplateDbContext _context;

    public KnowledgePointsController(TemplateDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取所有顶级知识点及其完整树状结构
    /// </summary>
    /// <returns>知识点树列表，支持任意深度嵌套</returns>
    /// <response code="200">成功返回知识点树列表</response>
    [HttpGet("all")]
    [ProducesResponseType(typeof(List<KnowledgePointDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetKnowledgePointAll()
    {
        // 获取所有知识点（用于后续构建树）
        var allKnowledgePoints = await _context.KnowledgePoints
            .AsNoTracking()
            .Include(c => c.Children)
            .ToListAsync();

        // 获取所有顶级知识点（ParentId为null）
        var rootPoints = allKnowledgePoints
            .Where(c => c.ParentId == null)
            .ToList();

        // 将平面列表转换为树状结构
        var result = rootPoints
            .Select(c => BuildKnowledgePointTree(c, allKnowledgePoints))
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// 获取指定知识点及其完整子树
    /// </summary>
    /// <param name="id">知识点ID</param>
    /// <returns>包含完整树状结构的知识点</returns>
    /// <response code="200">成功返回指定知识点及其子树</response>
    /// <response code="404">未找到指定知识点</response>
    [HttpGet("all/{id}")]
    [ProducesResponseType(typeof(KnowledgePointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKnowledgePoint(Guid id)
    {
        var knowledgePoint = await _context.KnowledgePoints
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null)
        {
            return NotFound(new { Message = "KnowledgePoint not found" });
        }

        // 获取所有知识点用于树构建
        var allKnowledgePoints = await _context.KnowledgePoints
            .AsNoTracking()
            .Include(c => c.Children)
            .ToListAsync();

        var result = BuildKnowledgePointTree(knowledgePoint, allKnowledgePoints);

        return Ok(result);
    }
    
    /// <summary>
    /// 获取单个知识点的详细内容
    /// </summary>
    /// <param name="id">知识点ID</param>
    /// <returns>知识点详细信息</returns>
    /// <response code="200">成功返回知识点内容</response>
    /// <response code="404">未找到指定知识点</response>
    [HttpGet("{id}")]
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
    /// 将知识点及其关联的子点递归转换为树状 DTO
    /// </summary>
    /// <param name="knowledgePoint">当前知识点实体</param>
    /// <param name="allPoints">所有知识点（用于查找子节点）</param>
    /// <returns>树状结构的 DTO</returns>
    private static KnowledgePointDto BuildKnowledgePointTree(
        KnowledgePoint knowledgePoint,
        List<KnowledgePoint> allPoints)
    {
        var childPoints = allPoints
            .Where(c => c.ParentId == knowledgePoint.Id)
            .ToList();

        return new KnowledgePointDto
        {
            Id = knowledgePoint.Id,
            Title = knowledgePoint.Title,
            Children = childPoints
                .Select(child => BuildKnowledgePointTree(child, allPoints))
                .ToList()
        };
    }
    
    /// <summary>
    /// 通过分类ID获取知识点及其相关题目
    /// </summary>
    /// <param name="categoryId">分类ID</param>
    /// <returns>知识点及题目列表</returns>
    /// <response code="200">成功返回知识点及题目列表</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<KnowledgePointWithQuestionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByCategory([FromQuery] Guid categoryId)
    {
        // 查询所有属于该分类的知识点
        var knowledgePoints = await _context.CategoryKnowledgePoints
            .Where(x => x.CategoryId == categoryId)
            .Include(x => x.KnowledgePoint)
                .ThenInclude(kp => kp.KnowledgePointQuestions)
                    .ThenInclude(kpq => kpq.Question)
            .Select(x => x.KnowledgePoint)
            .ToListAsync();

        var result = knowledgePoints.Select(kp => new KnowledgePointWithQuestionsDto
        {
            Id = kp.Id,
            Text = kp.Content,
            AudioUrl = kp.AudioUrl,
            Questions = kp.KnowledgePointQuestions
                .Select(kpq => kpq.Question)
                .Select(q => new QuestionContentDto
                {
                    Id = q.Id,
                    Text = q.Content,
                    Answer = q.StandardAnswer,
                }).ToList()
        }).ToList();

        return Ok(result);
    }

}