using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// Navigation for the independent course and question-category model.
/// These responses contain metadata only; serving question content must enforce its own access check.
/// </summary>
[Route("api/TotalCategories")]
[ApiController]
[WeChatUserOnly]
public class TotalCategoriesApiController(
    WeChatExamDbContext context,
    IPaperAccessService paperAccessService) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(List<TotalCategorySummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var courses = await context.TotalCategories.AsNoTracking()
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id)
            .Select(c => new TotalCategorySummaryDto(c.Id, c.Title, c.OrderIndex))
            .ToListAsync(cancellationToken);
        return Ok(courses);
    }

    [AllowAnonymous]
    [HttpGet("{totalCategoryId:guid}/QuestionCategories")]
    [ProducesResponseType(typeof(List<QuestionCategorySummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QuestionCategories(Guid totalCategoryId, CancellationToken cancellationToken)
    {
        if (!await context.TotalCategories.AnyAsync(c => c.Id == totalCategoryId, cancellationToken))
            return NotFound(new { Message = "Total category not found." });

        var status = await paperAccessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        // Real-exam rights and sale availability do not affect course practice rights.
        var categories = await context.QuestionCategories.AsNoTracking()
            .Where(c => c.TotalCategoryId == totalCategoryId)
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
        return Ok(categories.Select(c => new QuestionCategorySummaryDto(c.Id, c.TotalCategoryId, c.Title, c.OrderIndex,
            c.IsFree, QuestionAccess.CanPractice(c, status))).ToList());
    }

    [AllowAnonymous]
    [HttpGet("{totalCategoryId:guid}/KnowledgeCategories")]
    [ProducesResponseType(typeof(List<KnowledgeCategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> KnowledgeCategories(Guid totalCategoryId, CancellationToken cancellationToken)
    {
        if (!await context.TotalCategories.AnyAsync(c => c.Id == totalCategoryId, cancellationToken))
            return NotFound(new { Message = "Total category not found." });

        var status = await paperAccessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        var categories = await context.KnowledgeCategories.AsNoTracking()
            .Where(c => c.TotalCategoryId == totalCategoryId)
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id)
            .Select(c => new { Category = c, c.KnowledgeCategoryKnowledgePoints.Count })
            .ToListAsync(cancellationToken);
        var children = categories.ToLookup(c => c.Category.ParentKnowledgeCategoryId);
        List<KnowledgeCategoryDto> Branch(Guid? parent) => children[parent].Select(c =>
            new KnowledgeCategoryDto(c.Category.Id, c.Category.TotalCategoryId, c.Category.Title,
                c.Category.OrderIndex, c.Category.IsFree, KnowledgeAccess.CanRead(c.Category, status),
                c.Count, Branch(c.Category.Id))).ToList();
        return Ok(Branch(null));
    }
}
