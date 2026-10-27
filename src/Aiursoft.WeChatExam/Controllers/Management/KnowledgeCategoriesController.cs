using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class KnowledgeCategoriesController(WeChatExamDbContext context,
    KnowledgeExerciseValidation exerciseValidation,
    IStringLocalizer<KnowledgeCategoriesController> localizer) : Controller
{
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Knowledge Management", CascadedLinksIcon = "book-open",
        CascadedLinksOrder = 2, LinkText = "Knowledge Categories", LinkOrder = 0)]
    public async Task<IActionResult> Index(Guid? totalCategoryId)
    {
        var courses = await context.TotalCategories.OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ToListAsync();
        totalCategoryId ??= courses.FirstOrDefault()?.Id;
        if (totalCategoryId.HasValue && courses.All(c => c.Id != totalCategoryId)) return NotFound();
        var categories = await context.KnowledgeCategories
            .Include(c => c.KnowledgeCategoryKnowledgePoints).ThenInclude(c => c.KnowledgePoint)
            .Where(c => c.TotalCategoryId == totalCategoryId).OrderBy(c => c.OrderIndex).ThenBy(c => c.Id).ToListAsync();
        return this.StackView(new IndexViewModel
        {
            TotalCategoryId = totalCategoryId, TotalCategories = courses, Categories = categories,
            RootCategories = categories.Where(c => c.ParentKnowledgeCategoryId == null).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> UpdateOrder(Guid totalCategoryId, [FromBody] Guid[] ids)
    {
        var categories = await context.KnowledgeCategories.Where(c => c.TotalCategoryId == totalCategoryId).ToListAsync();
        if (ids.Distinct().Count() != ids.Length || !ids.ToHashSet().SetEquals(categories.Select(c => c.Id)))
            return BadRequest("The submitted directories do not match this course.");
        var positions = ids.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
        foreach (var category in categories) category.OrderIndex = positions[category.Id];
        await context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> UpdateKnowledgePointOrder(Guid id, [FromBody] Guid[] knowledgePointIds)
    {
        var links = await context.KnowledgeCategoryKnowledgePoints.Where(c => c.KnowledgeCategoryId == id).ToListAsync();
        if (knowledgePointIds.Distinct().Count() != knowledgePointIds.Length ||
            !knowledgePointIds.ToHashSet().SetEquals(links.Select(c => c.KnowledgePointId)))
            return BadRequest("The submitted knowledge points do not match this category.");
        var positions = knowledgePointIds.Select((key, index) => (key, index)).ToDictionary(x => x.key, x => x.index);
        foreach (var link in links) link.OrderIndex = positions[link.KnowledgePointId];
        await context.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Create(Guid? totalCategoryId)
    {
        var model = new CreateViewModel { TotalCategoryId = totalCategoryId ?? Guid.Empty };
        await LoadCreate(model);
        return this.StackView(model);
    }

    private async Task LoadCreate(CreateViewModel model)
    {
        model.TotalCategories = await context.TotalCategories.OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ToListAsync();
        if (model.TotalCategoryId == Guid.Empty) model.TotalCategoryId = model.TotalCategories.FirstOrDefault()?.Id ?? Guid.Empty;
        model.AvailableParents = await context.KnowledgeCategories.Include(c => c.TotalCategory)
            .Where(c => c.TotalCategoryId == model.TotalCategoryId).OrderBy(c => c.OrderIndex).ToListAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!await context.TotalCategories.AnyAsync(c => c.Id == model.TotalCategoryId))
            ModelState.AddModelError(nameof(model.TotalCategoryId), localizer["Select an existing total category."]);
        await ValidateParent(model.ParentKnowledgeCategoryId, model.TotalCategoryId, null);
        if (!ModelState.IsValid) { await LoadCreate(model); return this.StackView(model); }
        var category = new KnowledgeCategory
        {
            Title = model.Title.Trim(), TotalCategoryId = model.TotalCategoryId,
            ParentKnowledgeCategoryId = model.ParentKnowledgeCategoryId, IsFree = model.IsFree,
            OrderIndex = await NextOrder(model.TotalCategoryId, model.ParentKnowledgeCategoryId)
        };
        context.KnowledgeCategories.Add(category);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = category.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var category = await context.KnowledgeCategories.Include(c => c.TotalCategory).Include(c => c.ParentKnowledgeCategory)
            .Include(c => c.KnowledgeCategoryKnowledgePoints).ThenInclude(c => c.KnowledgePoint).SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        return this.StackView(new DetailsViewModel
        {
            Category = category,
            AssociatedKnowledgePoints = category.KnowledgeCategoryKnowledgePoints.OrderBy(c => c.OrderIndex)
                .ThenBy(c => c.KnowledgePoint.CreationTime).ThenBy(c => c.KnowledgePointId).Select(c => c.KnowledgePoint).ToList()
        });
    }

    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Edit(Guid id, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        var category = await context.KnowledgeCategories.Include(c => c.TotalCategory).SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        var model = new EditViewModel
        {
            Id = id, Title = category.Title, IsFree = category.IsFree,
            ParentKnowledgeCategoryId = category.ParentKnowledgeCategoryId
        };
        await LoadEdit(model, category);
        return this.StackView(model);
    }

    private async Task LoadEdit(EditViewModel model, KnowledgeCategory category)
    {
        // Moving an entire tree between courses is a separate data decision. An edit cannot
        // silently change its descendants' memberships or accept a forged course ID.
        model.TotalCategoryId = category.TotalCategoryId;
        model.TotalCategoryTitle = category.TotalCategory!.Title;
        model.AvailableParents = await context.KnowledgeCategories.Include(c => c.TotalCategory)
            .Where(c => c.TotalCategoryId == category.TotalCategoryId).OrderBy(c => c.OrderIndex).ToListAsync();
        var excluded = new HashSet<Guid> { category.Id };
        while (true)
        {
            var children = model.AvailableParents.Where(c => c.ParentKnowledgeCategoryId.HasValue &&
                excluded.Contains(c.ParentKnowledgeCategoryId.Value) && !excluded.Contains(c.Id)).Select(c => c.Id).ToArray();
            if (children.Length == 0) break;
            excluded.UnionWith(children);
        }
        model.AvailableParents.RemoveAll(c => excluded.Contains(c.Id));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id != model.Id) return NotFound();
        var category = await context.KnowledgeCategories.Include(c => c.TotalCategory).SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        await ValidateParent(model.ParentKnowledgeCategoryId, category.TotalCategoryId, id);
        if (model.IsFree && !category.IsFree)
        {
            var error = await exerciseValidation.ValidateFreeKnowledgeCategoryAsync(id);
            if (error != null) ModelState.AddModelError(nameof(model.IsFree), error);
        }
        if (!ModelState.IsValid) { await LoadEdit(model, category); return this.StackView(model); }
        if (category.ParentKnowledgeCategoryId != model.ParentKnowledgeCategoryId)
            category.OrderIndex = await NextOrder(category.TotalCategoryId, model.ParentKnowledgeCategoryId);
        category.Title = model.Title.Trim();
        category.IsFree = model.IsFree;
        category.ParentKnowledgeCategoryId = model.ParentKnowledgeCategoryId;
        await context.SaveChangesAsync();
        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<int> NextOrder(Guid course, Guid? parent) =>
        (await context.KnowledgeCategories.Where(c => c.TotalCategoryId == course && c.ParentKnowledgeCategoryId == parent)
            .Select(c => (int?)c.OrderIndex).MaxAsync() ?? -1) + 1;

    private async Task ValidateParent(Guid? parent, Guid course, Guid? editing)
    {
        var seen = new HashSet<Guid>();
        while (parent.HasValue)
        {
            if (parent == editing || !seen.Add(parent.Value))
            {
                ModelState.AddModelError(nameof(EditViewModel.ParentKnowledgeCategoryId), localizer["Cannot create circular reference"]);
                return;
            }
            var node = await context.KnowledgeCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == parent);
            if (node == null || node.TotalCategoryId != course)
            {
                ModelState.AddModelError(nameof(EditViewModel.ParentKnowledgeCategoryId), localizer["The parent must belong to the same total category."]);
                return;
            }
            parent = node.ParentKnowledgeCategoryId;
        }
    }

    private async Task<DeleteViewModel?> Deletion(Guid id)
    {
        var category = await context.KnowledgeCategories.SingleOrDefaultAsync(c => c.Id == id);
        return category == null ? null : new DeleteViewModel
        {
            Category = category,
            HasChildren = await context.KnowledgeCategories.AnyAsync(c => c.ParentKnowledgeCategoryId == id),
            HasKnowledgePoints = await context.KnowledgeCategoryKnowledgePoints.AnyAsync(c => c.KnowledgeCategoryId == id)
        };
    }

    [Authorize(Policy = AppPermissionNames.CanDeleteAnyCategory)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var model = await Deletion(id);
        return model == null ? NotFound() : this.StackView(model);
    }

    [HttpPost, ValidateAntiForgeryToken, ActionName("Delete")]
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyCategory)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var model = await Deletion(id);
        if (model == null) return NotFound();
        if (model.HasChildren || model.HasKnowledgePoints) return this.StackView(model);
        context.KnowledgeCategories.Remove(model.Category);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { model.Category.TotalCategoryId });
    }
}
