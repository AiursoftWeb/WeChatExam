using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class KnowledgePointsController(
    WeChatExamDbContext context,
    IPaperAccessService accessService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKnowledgeContent(Guid id, CancellationToken cancellationToken)
    {
        if (!await context.KnowledgePoints.AnyAsync(k => k.Id == id, cancellationToken))
            return NotFound(new { Message = "Knowledge point not found." });

        var placements = await context.KnowledgeCategoryKnowledgePoints.AsNoTracking()
            .Where(p => p.KnowledgePointId == id).Select(p => p.KnowledgeCategory)
            .ToListAsync(cancellationToken);
        var status = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        // A lesson can have several placements. One accessible placement grants the
        // same body; an unplaced draft grants none. An admin cookie is not a student JWT.
        if (!placements.Any(c => KnowledgeAccess.CanRead(c, status)))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active membership for a course containing this lesson is required.",
                RequiredTotalCategoryIds = placements.Select(c => c.TotalCategoryId).Distinct().ToList()
            });

        var content = await context.KnowledgePoints.AsNoTracking().Where(k => k.Id == id)
            .Select(k => new KnowledgeDto { Id = k.Id, Title = k.Title, Content = k.Content, AudioUrl = k.AudioUrl })
            .SingleAsync(cancellationToken);
        return Ok(content);
    }

    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(List<KnowledgePointWithQuestionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByCategory([FromQuery] Guid knowledgeCategoryId, CancellationToken cancellationToken)
    {
        if (knowledgeCategoryId == Guid.Empty || Request.Query.ContainsKey("categoryId"))
            return BadRequest(new { Message = "Specify knowledgeCategoryId." });
        var category = await context.KnowledgeCategories.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == knowledgeCategoryId, cancellationToken);
        if (category == null) return NotFound(new { Message = "Knowledge category not found." });

        var status = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (!KnowledgeAccess.CanRead(category, status))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active course membership is required.", RequiredTotalCategoryId = category.TotalCategoryId
            });

        var knowledgePoints = await context.KnowledgeCategoryKnowledgePoints.AsNoTracking()
            .Where(p => p.KnowledgeCategoryId == knowledgeCategoryId)
            .OrderBy(p => p.OrderIndex).ThenBy(p => p.KnowledgePoint.CreationTime).ThenBy(p => p.KnowledgePointId)
            .Include(p => p.KnowledgePoint).ThenInclude(k => k.KnowledgePointQuestions)
                .ThenInclude(p => p.Question).ThenInclude(q => q.QuestionCategory)
            .Select(p => p.KnowledgePoint)
            .ToListAsync(cancellationToken);

        return Ok(knowledgePoints.Select(k => new KnowledgePointWithQuestionsDto
        {
            Id = k.Id, Title = k.Title, Text = k.Content, AudioUrl = k.AudioUrl,
            Questions = k.KnowledgePointQuestions.OrderBy(p => p.QuestionId).Select(p =>
            {
                var allowed = QuestionAccess.CanPractice(p.Question.QuestionCategory!, status);
                // Keep the relationship visible without exposing a paid exercise through
                // a free lesson. Existing configuration conflicts must not bypass practice access.
                return new QuestionContentDto
                {
                    Id = p.QuestionId, HasAccess = allowed,
                    RequiredTotalCategoryId = p.Question.QuestionCategory!.TotalCategoryId,
                    Text = allowed ? p.Question.Content : string.Empty,
                    // Match server grading and clients that trim the selected option.
                    Answer = allowed ? p.Question.StandardAnswer.Trim() : string.Empty
                };
            }).ToList()
        }).ToList());
    }
}
