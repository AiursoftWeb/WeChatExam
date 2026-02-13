using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TaxonomiesViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanManageTags)] // Assuming managing taxonomies requires same permission as tags
public class TaxonomiesController(WeChatExamDbContext context, ITaxonomyService taxonomyService) : Controller
{
        // GET: Taxonomies
        [RenderInNavBar(
            NavGroupName = "Administration",
            NavGroupOrder = 9999,
            CascadedLinksGroupName = "Content Management",
            CascadedLinksIcon = "folder-tree",
            CascadedLinksOrder = 1,
            LinkText = "Taxonomies",
            LinkOrder = 7)]
        public async Task<IActionResult> Index()    {
        var taxonomies = await taxonomyService.GetAllTaxonomiesAsync();
        var tagCounts = new Dictionary<int, int>();

        foreach (var taxonomy in taxonomies)
        {
            var count = await context.Tags.CountAsync(t => t.TaxonomyId == taxonomy.Id);
            tagCounts[taxonomy.Id] = count;
        }

        return this.StackView(new IndexViewModel
        {
            Taxonomies = taxonomies,
            TagCounts = tagCounts
        });
    }

    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        await taxonomyService.AddTaxonomyAsync(model.Name);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var taxonomy = await taxonomyService.GetTaxonomyByIdAsync(id.Value);
        if (taxonomy == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = taxonomy.Id,
            Name = taxonomy.Name
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var taxonomy = await taxonomyService.GetTaxonomyByIdAsync(model.Id);
        if (taxonomy == null) return NotFound();

        taxonomy.Name = model.Name;
        await taxonomyService.UpdateTaxonomyAsync(taxonomy);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var taxonomy = await taxonomyService.GetTaxonomyByIdAsync(id.Value);
        if (taxonomy == null) return NotFound();

        var tagCount = await context.Tags.CountAsync(t => t.TaxonomyId == id.Value);

        return this.StackView(new DeleteViewModel
        {
            Taxonomy = taxonomy,
            TagCount = tagCount
        });
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await taxonomyService.DeleteTaxonomyAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
