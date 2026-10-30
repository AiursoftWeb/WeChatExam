using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.InstitutionsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditPapers)]
public class InstitutionsController(WeChatExamDbContext context) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Real Exam Management",
        CascadedLinksIcon = "university",
        CascadedLinksOrder = 50,
        LinkText = "Institutions",
        LinkOrder = 0)]
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        search = search?.Trim();
        var query = context.Institutions.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(i => i.Title.Contains(search));
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        return this.StackView(new IndexViewModel
        {
            Search = search, Page = page, Total = total,
            Institutions = await query.OrderBy(i => i.OrderIndex).ThenBy(i => i.Title).ThenBy(i => i.Id)
                .Skip((page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToListAsync()
        });
    }

    public IActionResult Create()
    {
        return this.StackView(new EditViewModel { PageTitle = "Create Institution" }, nameof(Edit));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        // A posted ID must never turn creation into editing an existing institution.
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Institution";
        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Edit));
        }

        context.Institutions.Add(new Institution
        {
            Title = model.Title.Trim(),
            OrderIndex = model.OrderIndex
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var institution = await context.Institutions.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
        if (institution == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = institution.Id,
            Title = institution.Title,
            OrderIndex = institution.OrderIndex
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();
        var institution = await context.Institutions.SingleOrDefaultAsync(c => c.Id == id);
        if (institution == null) return NotFound();
        if (!ModelState.IsValid) return this.StackView(model);

        institution.Title = model.Title.Trim();
        institution.OrderIndex = model.OrderIndex;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
