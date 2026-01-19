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
public class TagsController(WeChatExamDbContext context, ITagService tagService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Tags",
        LinkOrder = 3)]
    public async Task<IActionResult> Index(string? search)
    {
        var tags = string.IsNullOrWhiteSpace(search)
            ? await tagService.GetAllTagsAsync()
            : await tagService.SearchTagsAsync(search);

        // Get usage counts for each tag
        var tagUsageCounts = new Dictionary<int, int>();
        foreach (var tag in tags)
        {
            var count = await context.QuestionTags.CountAsync(qt => qt.TagId == tag.Id);
            tagUsageCounts[tag.Id] = count;
        }

        return this.StackView(new IndexViewModel
        {
            Tags = tags,
            TagUsageCounts = tagUsageCounts,
            SearchQuery = search
        });
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
