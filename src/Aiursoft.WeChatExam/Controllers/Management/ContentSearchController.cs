using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ContentSearchViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanReadQuestions)]
[LimitPerMin]
public class ContentSearchController(WeChatExamDbContext db, ContentVectorSearchService search) : Controller
{
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management", CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1, LinkText = "Search Content", LinkOrder = 0)]
    public async Task<IActionResult> Index(string? query, string kind = "all", Guid? categoryId = null)
    {
        var model = new IndexViewModel
        {
            Query = query?.Trim() ?? string.Empty, Kind = kind, CategoryId = categoryId,
            Categories = await db.Categories.AsNoTracking().OrderBy(c => c.Title).ToListAsync(HttpContext.RequestAborted)
        };
        if (model.Query.Length > 500 || kind is not ("all" or "questions" or "knowledge")) return BadRequest();
        (model.Results, model.UsedAi) = await search.SearchAsync(model.Query, kind, categoryId,
            cancellationToken: HttpContext.RequestAborted);
        return this.StackView(model);
    }

    public async Task<IActionResult> Questions(string? query, QuestionType? questionType, Guid? categoryId,
        bool useAi = true, bool includeSearchStatus = false)
    {
        if (query?.Length > 500) return BadRequest();
        var (results, usedAi) = await search.SearchAsync(query?.Trim() ?? string.Empty, "questions", categoryId,
            questionType, useAi, cancellationToken: HttpContext.RequestAborted);
        return includeSearchStatus ? Json(new { results, usedAi }) : Json(results);
    }

    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> KnowledgePoints(string? query, bool useAi = true)
    {
        if (query?.Length > 500) return BadRequest();
        var (results, usedAi) = await search.SearchAsync(query?.Trim() ?? string.Empty, "knowledge", null,
            useAi: useAi, cancellationToken: HttpContext.RequestAborted);
        return Json(new { results, usedAi });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> RecommendedKnowledgePoints([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!await db.Questions.AnyAsync(q => q.Id == request.Id, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.RecommendKnowledgePointsAsync(request, HttpContext.RequestAborted);
        return Json(new { available, results });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> RecommendedQuestions([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!await db.KnowledgePoints.AnyAsync(k => k.Id == request.Id, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.RecommendAsync(request, true, HttpContext.RequestAborted);
        return Json(new { available, results });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    public async Task<IActionResult> SimilarQuestions([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!await db.Questions.AnyAsync(q => q.Id == request.Id, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.RecommendAsync(request, false, HttpContext.RequestAborted);
        return Json(new { available, results });
    }
}
