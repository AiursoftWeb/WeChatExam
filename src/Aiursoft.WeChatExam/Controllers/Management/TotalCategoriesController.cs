using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TotalCategoriesViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
public class TotalCategoriesController(WeChatExamDbContext context) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1,
        LinkText = "Total Categories",
        LinkOrder = 0)]
    public async Task<IActionResult> Index()
    {
        return this.StackView(new IndexViewModel
        {
            Categories = await context.TotalCategories.AsNoTracking()
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync()
        });
    }

    public IActionResult Create()
    {
        return this.StackView(new EditViewModel { PageTitle = "Create Total Category" }, nameof(Edit));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        // A posted ID must never turn creation into editing an existing course.
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Total Category";
        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Edit));
        }

        context.TotalCategories.Add(new TotalCategory
        {
            Title = model.Title.Trim(),
            OrderIndex = model.OrderIndex
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var category = await context.TotalCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = category.Id,
            Title = category.Title,
            OrderIndex = category.OrderIndex
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();
        var category = await context.TotalCategories.SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        if (!ModelState.IsValid) return this.StackView(model);

        category.Title = model.Title.Trim();
        category.OrderIndex = model.OrderIndex;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
