using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MockPapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditPapers)]
public class MockPapersController(WeChatExamDbContext context, IStringLocalizer<MockPapersController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Mock Exam Management",
        CascadedLinksIcon = "notebook-pen",
        CascadedLinksOrder = 3,
        LinkText = "Mock Papers",
        LinkOrder = 0)]
    public async Task<IActionResult> Index(string? search, Guid? totalCategoryId, int page = 1)
    {
        search = search?.Trim();
        var query = context.MockPapers.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(p => p.Title.Contains(search));
        if (totalCategoryId.HasValue) query = query.Where(p => p.TotalCategoryId == totalCategoryId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        return this.StackView(new IndexViewModel
        {
            Search = search, TotalCategoryId = totalCategoryId, Page = page, Total = total,
            Courses = await Courses(),
            Papers = await query.Include(p => p.TotalCategory).OrderByDescending(p => p.CreationTime).ThenBy(p => p.Id)
                .Skip((page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToListAsync()
        });
    }

    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create() => this.StackView(new EditViewModel
    {
        PageTitle = "Create Mock Paper", Courses = await Courses()
    }, nameof(Edit));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        // Creation always creates a new draft; posted IDs or status cannot publish or replace a paper.
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Mock Paper";
        await ValidateCourse(model);
        if (!ModelState.IsValid) return this.StackView(model, nameof(Edit));

        context.MockPapers.Add(new MockPaper
        {
            Title = model.Title.Trim(), TotalCategoryId = model.TotalCategoryId!.Value,
            TimeLimit = model.TimeLimit, AllowViewAnonymousely = model.AllowViewAnonymousely
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var paper = await context.MockPapers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        return this.StackView(new EditViewModel
        {
            Id = paper.Id, Title = paper.Title, TotalCategoryId = paper.TotalCategoryId,
            TimeLimit = paper.TimeLimit, AllowViewAnonymousely = paper.AllowViewAnonymousely,
            Courses = await Courses()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        await ValidateCourse(model);
        if (!ModelState.IsValid) return this.StackView(model);

        paper.Title = model.Title.Trim();
        paper.TotalCategoryId = model.TotalCategoryId!.Value;
        paper.TimeLimit = model.TimeLimit;
        paper.AllowViewAnonymousely = model.AllowViewAnonymousely;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private Task<List<TotalCategory>> Courses() => context.TotalCategories.AsNoTracking()
        .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();

    private async Task ValidateCourse(EditViewModel model)
    {
        model.Courses = await Courses();
        if (model.TotalCategoryId.HasValue && model.Courses.All(c => c.Id != model.TotalCategoryId.Value))
            ModelState.AddModelError(nameof(model.TotalCategoryId), localizer["Select an existing course."]);
    }
}
