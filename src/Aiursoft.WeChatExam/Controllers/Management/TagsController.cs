using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TagsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
public class TagsController(
    WeChatExamDbContext context, 
    ITagService tagService,
    ITaxonomyService taxonomyService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Tags",
        LinkOrder = 3)]
    public async Task<IActionResult> Index(string? search, int? taxonomyId)
    {
        var tags = await tagService.SearchTagsAsync(search, taxonomyId);

        // Get usage counts for each tag
        var tagUsageCounts = new Dictionary<int, int>();
        foreach (var tag in tags)
        {
            var count = await context.QuestionTags.CountAsync(qt => qt.TagId == tag.Id);
            tagUsageCounts[tag.Id] = count;
        }

        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync();

        return this.StackView(new IndexViewModel
        {
            Tags = tags,
            AllTaxonomies = taxonomies,
            TagUsageCounts = tagUsageCounts,
            SearchQuery = search,
            TaxonomyId = taxonomyId,
            AvailableTaxonomies = taxonomies.Select(t => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = t.Id.ToString(),
                Text = t.Name,
                Selected = t.Id == taxonomyId
            })
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var tag = await tagService.GetTagByIdAsync(id.Value);
        if (tag == null) return NotFound();

        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync();

        return this.StackView(new EditViewModel
        {
            Id = tag.Id,
            DisplayName = tag.DisplayName,
            TaxonomyId = tag.TaxonomyId,
            AvailableTaxonomies = taxonomies.Select(t => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = t.Id.ToString(),
                Text = t.Name
            })
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var taxonomies = await taxonomyService.GetAllTaxonomiesAsync();
            model.AvailableTaxonomies = taxonomies.Select(t => new Microsoft.AspNetCore.Mvc.Rendering.SelectListItem
            {
                Value = t.Id.ToString(),
                Text = t.Name
            });
            return this.StackView(model);
        }

        var tag = await tagService.GetTagByIdAsync(model.Id);
        if (tag == null) return NotFound();

        tag.DisplayName = model.DisplayName;
        tag.NormalizedName = model.DisplayName.Trim().ToUpperInvariant();
        tag.TaxonomyId = model.TaxonomyId;

        await tagService.UpdateTagAsync(tag);

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var tag = await tagService.GetTagByIdAsync(id.Value);
        if (tag == null) return NotFound();

        var usageCount = await context.QuestionTags.CountAsync(qt => qt.TagId == id.Value);

        return this.StackView(new DeleteViewModel
        {
            Tag = tag,
            UsageCount = usageCount
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageTags)]
    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await tagService.DeleteTagAsync(id);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Autocomplete(string query)
    {
        var tags = await tagService.SearchTagsAsync(query);
        var result = tags.Select(t => new { t.Id, t.DisplayName }).Take(10).ToList();
        return Json(result);
    }
}
