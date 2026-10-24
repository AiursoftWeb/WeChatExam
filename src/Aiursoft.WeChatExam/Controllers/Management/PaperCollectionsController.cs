using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PaperCollectionsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanEditPapers)]
[LimitPerMin]
public class PaperCollectionsController(WeChatExamDbContext db, PaperCollectionService collections,
    IStringLocalizer<PaperCollectionsController> localizer) : Controller
{
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management", CascadedLinksIcon = "clipboard-list", CascadedLinksOrder = 2,
        LinkText = "Paper Collections", LinkOrder = 2)]
    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Index()
    {
        var cards = await db.PaperCollections.AsNoTracking().OrderBy(c => c.IsArchived).ThenBy(c => c.OrderIndex).ThenBy(c => c.Name)
            .Select(c => new CollectionCard(c, c.Papers.Count,
                c.Papers.Count(p => (p.Paper.Status == PaperStatus.Publishable || p.Paper.Status == PaperStatus.Frozen) && p.Paper.PaperSnapshots.Any()),
                c.Papers.Count(p => p.Paper.Status == PaperStatus.Draft))).ToListAsync();
        return this.StackView(new IndexViewModel { Connected = await db.LegacyPaperCollectionCatalogs.AnyAsync(), Collections = cards });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Setup(int? taxonomyId)
    {
        if (await db.LegacyPaperCollectionCatalogs.AnyAsync()) return RedirectToAction(nameof(Index));
        var model = new SetupViewModel { TaxonomyId = taxonomyId, Taxonomies = await db.Taxonomies.AsNoTracking().OrderBy(t => t.Name).ToListAsync() };
        if (taxonomyId.HasValue)
        {
            try { model.Preview = await collections.PreviewAsync(taxonomyId.Value); model.Fingerprint = model.Preview.Fingerprint; }
            catch (InvalidOperationException e) { ModelState.AddModelError(string.Empty, e.Message); }
        }
        return this.StackView(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Connect(int taxonomyId, string fingerprint)
    {
        try { await collections.ImportAsync(taxonomyId, fingerprint); }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; return RedirectToAction(nameof(Setup), new { taxonomyId }); }
        catch (DbUpdateException) { TempData["Error"] = localizer["The directory changed while saving. Reload and try again."].Value; return RedirectToAction(nameof(Setup), new { taxonomyId }); }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Create()
    {
        if (!await db.LegacyPaperCollectionCatalogs.AnyAsync()) return RedirectToAction(nameof(Setup));
        return this.StackView(new EditViewModel(), "Edit");
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Edit(Guid id, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        var c = await db.PaperCollections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var model = new EditViewModel { Id = id, Revision = c.Revision, Name = c.Name, OrderIndex = c.OrderIndex,
            IsVisible = c.IsVisible, IsArchived = c.IsArchived,
            PaperIds = await db.PaperCollectionPapers.Where(p => p.CollectionId == id).Select(p => p.PaperId).ToListAsync() };
        await FillAsync(model);
        return this.StackView(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Save(EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (ModelState.IsValid)
        {
            try
            {
                var id = await collections.SaveAsync(model.Id, model.Revision, model.Name, model.OrderIndex, model.IsVisible, model.IsArchived, model.PaperIds);
                if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);
                return RedirectToAction(nameof(Edit), new { id });
            }
            catch (InvalidOperationException e) { ModelState.AddModelError(string.Empty, e.Message); }
            catch (DbUpdateConcurrencyException) { ModelState.AddModelError(string.Empty, localizer["This collection changed. Reload before saving."]); }
            catch (DbUpdateException) { ModelState.AddModelError(string.Empty, localizer["The directory changed while saving. Reload and try again."]); }
        }
        db.ChangeTracker.Clear();
        await FillAsync(model);
        return this.StackView(model, "Edit");
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Papers(Guid id, string? search, int page = 1)
    {
        var c = await db.PaperCollections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        page = Math.Clamp(page, 1, 100000);
        var members = db.PaperCollectionPapers.Where(p => p.CollectionId == id).Select(p => p.PaperId);
        var query = db.Papers.AsNoTracking().Where(p => !members.Contains(p.Id));
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(p => p.Title.Contains(search));
        var rows = await query.OrderBy(p => p.Title).ThenBy(p => p.Id).Skip((page - 1) * 24).Take(25)
            .Select(p => new CollectionPaper(p.Id, p.Title, p.Status, p.IsFree, p.IsRealExam, p.PaperSnapshots.Any())).ToListAsync();
        return this.StackView(new PapersViewModel { Id = id, Revision = c.Revision, Name = c.Name, Search = search,
            Page = page, HasMore = rows.Count > 24, Papers = rows.Take(24).ToList() });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> AddPapers(Guid id, Guid revision, Guid[]? paperIds)
    {
        var c = await db.PaperCollections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var members = await db.PaperCollectionPapers.Where(p => p.CollectionId == id).Select(p => p.PaperId).ToListAsync();
        try
        {
            if (c.IsArchived) throw new InvalidOperationException(localizer["Restore the collection before adding exam papers."]);
            await collections.SaveAsync(id, revision, c.Name, c.OrderIndex, c.IsVisible, c.IsArchived, members.Concat(paperIds ?? []));
        }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        catch (DbUpdateException) { TempData["Error"] = localizer["The directory changed while saving. Reload and try again."].Value; }
        return RedirectToAction(nameof(Edit), new { id });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Preview(Guid id)
    {
        var c = await db.PaperCollections.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (c == null) return NotFound();
        var ids = db.PaperCollectionPapers.Where(p => p.CollectionId == id).Select(p => p.PaperId);
        var rows = await PaperCollectionService.Available(db.Papers.AsNoTracking()).Where(p => ids.Contains(p.Id) && p.IsRealExam)
            .OrderByDescending(p => p.IsFree).ThenBy(p => p.Title).ThenBy(p => p.Id)
            .Select(p => new CollectionPaper(p.Id, p.Title, p.Status, p.IsFree, p.IsRealExam, true)).ToListAsync();
        return this.StackView(new EditViewModel { Id = id, Name = c.Name, IsVisible = c.IsVisible, IsArchived = c.IsArchived, Papers = rows }, "Preview");
    }

    private async Task FillAsync(EditViewModel model)
    {
        model.Papers = await db.Papers.AsNoTracking().Where(p => model.PaperIds.Contains(p.Id)).OrderBy(p => p.Title).ThenBy(p => p.Id)
            .Select(p => new CollectionPaper(p.Id, p.Title, p.Status, p.IsFree, p.IsRealExam, p.PaperSnapshots.Any())).ToListAsync();
        if (model.Id.HasValue)
        {
            model.LegacyTagId = await db.LegacyPaperCollectionBindings.Where(b => b.CollectionId == model.Id).Select(b => (int?)b.TagId).SingleOrDefaultAsync();
            model.SharedQuestionCount = await db.QuestionTags.CountAsync(q => q.TagId == model.LegacyTagId);
        }
    }
}
