using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi.KnowledgePointDtos;
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

    // GET: api/knowledgePoints/all
    [HttpGet("all")]
    public async Task<IActionResult> GetKnowledgePointAll()
    {
        var knowledgePoints = await _context.KnowledgePoints
            .Include(c => c.Children)
            .Where(c => c.ParentId == null)
            .Select(c => new KnowledgePointDto
            {
                Id = c.Id,
                Title = c.Title,
                Children = c.Children.Select(child => new Child
                {
                    Id = child.Id,
                    Title = child.Title
                }).ToArray()
            })
            .ToListAsync();

        return Ok(knowledgePoints);
    }

    // GET: api/knowledgePoints/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetKnowledgePoint(Guid id)
    {
        var knowledgePoint = await _context.KnowledgePoints
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null)
        {
            return NotFound(new { Message = "KnowledgePoint not found" });
        }

        var knowledgePointDto = new KnowledgePointDto
        {
            Id = knowledgePoint.Id,
            Title = knowledgePoint.Title,
            Children = knowledgePoint.Children.Select(child => new Child
            {
                Id = child.Id,
                Title = child.Title
            }).ToArray()
        };

        return Ok(knowledgePointDto);
    }


}