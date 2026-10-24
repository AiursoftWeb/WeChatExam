using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TagsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class TagsController(
    WeChatExamDbContext context, 
    ITagService tagService,
    ITaxonomyService taxonomyService,
    IStringLocalizer<TagsController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1,
        LinkText = "Tags",
        LinkOrder = 6)]
    public async Task<IActionResult> Index(string? search, int? taxonomyId)
    {
        var tags = await tagService.SearchTagsAsync(search, taxonomyId);

        var tagUsageCounts = await context.QuestionTags.GroupBy(q => q.TagId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);
        var paperUsageCounts = await context.PaperTags.GroupBy(q => q.TagId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count);
        var collectionsByTag = await context.LegacyPaperCollectionBindings.ToDictionaryAsync(b => b.TagId, b => b.CollectionId);
        foreach (var tag in tags) tagUsageCounts.TryAdd(tag.Id, 0);

        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(includeCategory: true);

        return this.StackView(new IndexViewModel
        {
            Tags = tags,
            AllTaxonomies = taxonomies,
            TagUsageCounts = tagUsageCounts,
            PaperUsageCounts = paperUsageCounts,
            CollectionsByTag = collectionsByTag,
            SearchQuery = search,
            TaxonomyId = taxonomyId,
            AvailableTaxonomies = taxonomies.Select(t =>
            {
                var firstCategory = t.CategoryTaxonomies.FirstOrDefault()?.Category;
                return new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = firstCategory != null ? $"{t.Name} ({firstCategory.Title})" : t.Name,
                    Selected = t.Id == taxonomyId
                };
            })
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Create()
    {
        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(includeCategory: true);

        return this.StackView(new CreateViewModel
        {
            AvailableTaxonomies = taxonomies.Select(t =>
            {
                var firstCategory = t.CategoryTaxonomies.FirstOrDefault()?.Category;
                return new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = firstCategory != null ? $"{t.Name} ({firstCategory.Title})" : t.Name
                };
            })
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(includeCategory: true);
            model.AvailableTaxonomies = taxonomies.Select(t =>
            {
                var firstCategory = t.CategoryTaxonomies.FirstOrDefault()?.Category;
                return new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = firstCategory != null ? $"{t.Name} ({firstCategory.Title})" : t.Name
                };
            });
            return this.StackView(model);
        }

        var tag = new Tag
        {
            DisplayName = model.DisplayName,
            NormalizedName = model.DisplayName.Trim().ToUpperInvariant(),
            TaxonomyId = model.TaxonomyId,
            IsFree = model.IsFree,
            OrderIndex = (await context.Tags.Where(t => t.TaxonomyId == model.TaxonomyId).MaxAsync(t => (int?)t.OrderIndex) ?? -1) + 1
        };

        try { await tagService.CreateTagAsync(tag); }
        catch (InvalidOperationException e)
        {
            context.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, e.Message);
            model.AvailableTaxonomies = (await taxonomyService.GetAllTaxonomiesAsync()).Select(t => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(t.Name, t.Id.ToString()));
            return this.StackView(model);
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    public async Task<IActionResult> UpdateOrder([FromBody] int[] ids)
    {
        if (await context.LegacyPaperCollectionBindings.AnyAsync(b => ids.Contains(b.TagId)))
            return Conflict(localizer["Reorder these entries in Paper Collections."].Value);

        for (var i = 0; i < ids.Length; i++)
        {
            var tag = await context.Tags.FindAsync(ids[i]);
            if (tag != null)
            {
                tag.OrderIndex = i;
            }
        }
        await context.SaveChangesAsync();
        return Ok();
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Edit(int? id, bool fees = false, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id == null) return NotFound();

        var tag = await tagService.GetTagByIdAsync(id.Value);
        if (tag == null) return NotFound();

        var collectionId = await context.LegacyPaperCollectionBindings.Where(b => b.TagId == id).Select(b => (Guid?)b.CollectionId).SingleOrDefaultAsync();
        if (collectionId.HasValue && !fees && !Url.IsLocalUrl(checkReturnUrl)) return RedirectToAction("Edit", "PaperCollections", new { id = collectionId });

        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(includeCategory: true);

        return this.StackView(new EditViewModel
        {
            CollectionId = collectionId,
            Id = tag.Id,
            DisplayName = tag.DisplayName,
            TaxonomyId = tag.TaxonomyId,
            IsFree = tag.IsFree,
            AvailableTaxonomies = taxonomies.Select(t =>
            {
                var firstCategory = t.CategoryTaxonomies.FirstOrDefault()?.Category;
                return new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = firstCategory != null ? $"{t.Name} ({firstCategory.Title})" : t.Name
                };
            })
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel model, string? checkReturnUrl = null)
    {
        var mapped = await context.LegacyPaperCollectionBindings.Include(b => b.Tag).SingleOrDefaultAsync(b => b.TagId == model.Id);
        if (mapped != null)
        {
            model.CollectionId = mapped.CollectionId;
            if (model.DisplayName != mapped.Tag.DisplayName || model.TaxonomyId != mapped.Tag.TaxonomyId)
                return Conflict(localizer["Edit directory names and taxonomy through the paper collection editor."].Value);
        }

        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (!ModelState.IsValid)
        {
            var taxonomies = await taxonomyService.GetAllTaxonomiesAsync(includeCategory: true);
            model.AvailableTaxonomies = taxonomies.Select(t =>
            {
                var firstCategory = t.CategoryTaxonomies.FirstOrDefault()?.Category;
                return new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
                {
                    Value = t.Id.ToString(),
                    Text = firstCategory != null ? $"{t.Name} ({firstCategory.Title})" : t.Name
                };
            });
            return this.StackView(model);
        }

        var tag = await tagService.GetTagByIdAsync(model.Id);
        if (tag == null) return NotFound();

        if (tag.TaxonomyId != model.TaxonomyId)
        {
            tag.OrderIndex = (await context.Tags.Where(t => t.TaxonomyId == model.TaxonomyId).MaxAsync(t => (int?)t.OrderIndex) ?? -1) + 1;
        }

        tag.DisplayName = model.DisplayName;
        tag.NormalizedName = model.DisplayName.Trim().ToUpperInvariant();
        tag.TaxonomyId = model.TaxonomyId;
        tag.IsFree = model.IsFree;

        try { await tagService.UpdateTagAsync(tag); }
        catch (InvalidOperationException e)
        {
            context.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, e.Message);
            model.AvailableTaxonomies = (await taxonomyService.GetAllTaxonomiesAsync()).Select(t => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem(t.Name, t.Id.ToString()));
            return this.StackView(model);
        }

        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var tag = await tagService.GetTagByIdAsync(id.Value);
        if (tag == null) return NotFound();

        var collectionId = await context.LegacyPaperCollectionBindings.Where(b => b.TagId == id).Select(b => (Guid?)b.CollectionId).SingleOrDefaultAsync();
        if (collectionId.HasValue) return RedirectToAction("Edit", "PaperCollections", new { id = collectionId });
        var usageCount = await context.QuestionTags.CountAsync(qt => qt.TagId == id.Value);

        return this.StackView(new DeleteViewModel
        {
            Tag = tag,
            UsageCount = usageCount,
            PaperUsageCount = await context.PaperTags.CountAsync(t => t.TagId == id)
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        try { await tagService.DeleteTagAsync(id); }
        catch (InvalidOperationException e) { TempData["Error"] = e.Message; }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
    {
        if (!request.TagIds.Any())
        {
            return BadRequest("No tags selected for deletion.");
        }

        var deletedIds = new List<int>();
        foreach (var tagId in request.TagIds)
        {
            try
            {
                await tagService.DeleteTagAsync(tagId);
                deletedIds.Add(tagId);
            }
            catch (Exception)
            {
                // Skip tags that cannot be deleted (e.g. maybe due to constraints if any, though DeleteTagAsync should handle it)
            }
        }

        return Json(new BatchDeleteResult
        {
            DeletedCount = deletedIds.Count,
            DeletedIds = deletedIds.ToArray()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Autocomplete(string query)
    {
        var tags = await tagService.SearchTagsAsync(query);
        var result = tags.Select(t => new { t.Id, t.DisplayName }).Take(10).ToList();
        return Json(result);
    }
}
