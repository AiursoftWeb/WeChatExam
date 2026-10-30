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
        CascadedLinksGroupName = "Advanced Management", CascadedLinksIcon = "settings",
        CascadedLinksOrder = 110, LinkText = "Search Content", LinkOrder = 1)]
    public async Task<IActionResult> Index(string? query, string kind = "all",
        Guid? knowledgeCategoryId = null, Guid? questionCategoryId = null)
    {
        if (!ModelState.IsValid || query?.Length > 500 || kind is not ("all" or "questions" or "knowledge"))
            return BadRequest();
        var model = new IndexViewModel
        {
            Query = query?.Trim() ?? string.Empty, Kind = kind,
            KnowledgeCategoryId = knowledgeCategoryId, QuestionCategoryId = questionCategoryId,
            KnowledgeCategories = await db.KnowledgeCategories.AsNoTracking().Include(c => c.TotalCategory).OrderBy(c => c.Title)
                .ToListAsync(HttpContext.RequestAborted),
            QuestionCategories = await db.QuestionCategories.AsNoTracking().Include(c => c.TotalCategory)
                .OrderBy(c => c.TotalCategory!.OrderIndex).ThenBy(c => c.TotalCategoryId)
                .ThenBy(c => c.OrderIndex).ThenBy(c => c.Title).ToListAsync(HttpContext.RequestAborted)
        };
        if (knowledgeCategoryId.HasValue && model.KnowledgeCategories.All(c => c.Id != knowledgeCategoryId) ||
            questionCategoryId.HasValue && model.QuestionCategories.All(c => c.Id != questionCategoryId))
            return NotFound();
        (model.Results, model.UsedAi) = await search.SearchAsync(model.Query, kind, knowledgeCategoryId,
            cancellationToken: HttpContext.RequestAborted, questionCategoryId: questionCategoryId);
        return this.StackView(model);
    }

    public async Task<IActionResult> Questions(string? query, QuestionType? questionType, Guid? questionCategoryId,
        bool useAi = true, bool includeSearchStatus = false)
    {
        if (!ModelState.IsValid || query?.Length > 500) return BadRequest();
        if (questionCategoryId.HasValue && !await db.QuestionCategories.AnyAsync(c => c.Id == questionCategoryId,
                HttpContext.RequestAborted)) return NotFound();
        var (results, usedAi) = await search.SearchAsync(query?.Trim() ?? string.Empty, "questions", null,
            questionType, useAi, cancellationToken: HttpContext.RequestAborted, questionCategoryId: questionCategoryId);
        return includeSearchStatus ? Json(new { results, usedAi }) : Json(results);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> SimilarKnowledgePointDrafts([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid || request.Id != Guid.Empty || string.IsNullOrWhiteSpace(request.Text)) return BadRequest();
        var (available, results) = await search.SimilarKnowledgePointsAsync(request, HttpContext.RequestAborted);
        return Json(new { available, results });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> SimilarKnowledgePoints([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!await db.KnowledgePoints.AnyAsync(k => k.Id == request.Id, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.SimilarKnowledgePointsAsync(request, HttpContext.RequestAborted);
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
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public async Task<IActionResult> SimilarQuestionDrafts([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid || request.Id != Guid.Empty || string.IsNullOrWhiteSpace(request.Text)) return BadRequest();
        if (request.QuestionCategoryId.HasValue && !await db.QuestionCategories.AnyAsync(
                c => c.Id == request.QuestionCategoryId, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.RecommendAsync(request, false, HttpContext.RequestAborted);
        return Json(new { available, results });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    public async Task<IActionResult> SimilarQuestions([FromBody] RecommendationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest();
        if (!await db.Questions.AnyAsync(q => q.Id == request.Id, HttpContext.RequestAborted)) return NotFound();
        if (request.QuestionCategoryId.HasValue && !await db.QuestionCategories.AnyAsync(
                c => c.Id == request.QuestionCategoryId, HttpContext.RequestAborted)) return NotFound();
        var (available, results) = await search.RecommendAsync(request, false, HttpContext.RequestAborted);
        return Json(new { available, results });
    }
}
