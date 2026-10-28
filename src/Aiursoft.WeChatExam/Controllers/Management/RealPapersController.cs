using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.RealPapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditPapers)]
public class RealPapersController(WeChatExamDbContext context, IStringLocalizer<RealPapersController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Real Exam Management",
        CascadedLinksIcon = "university",
        CascadedLinksOrder = 4,
        LinkText = "Real Papers",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? search, Guid? institutionId, int page = 1)
    {
        search = search?.Trim();
        var query = context.RealPapers.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(p => p.Title.Contains(search));
        if (institutionId.HasValue) query = query.Where(p => p.InstitutionId == institutionId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        return this.StackView(new IndexViewModel
        {
            Search = search, InstitutionId = institutionId, Page = page, Total = total,
            Institutions = await Institutions(),
            Papers = await query.Include(p => p.Institution).OrderByDescending(p => p.CreationTime).ThenBy(p => p.Id)
                .Skip((page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToListAsync()
        });
    }

    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create(Guid? institutionId) => this.StackView(new EditViewModel
    {
        PageTitle = "Create Real Paper", InstitutionId = institutionId, Institutions = await Institutions()
    }, nameof(Edit));

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        // Creation always creates a new draft; posted IDs or status cannot publish or replace a paper.
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Real Paper";
        await ValidateInstitution(model);
        if (!ModelState.IsValid) return this.StackView(model, nameof(Edit));

        context.RealPapers.Add(new RealPaper
        {
            Title = model.Title.Trim(), InstitutionId = model.InstitutionId!.Value,
            TimeLimit = model.TimeLimit
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var paper = await context.RealPapers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        return this.StackView(new EditViewModel
        {
            Id = paper.Id, Title = paper.Title, InstitutionId = paper.InstitutionId,
            TimeLimit = paper.TimeLimit,
            Institutions = await Institutions()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();
        var paper = await context.RealPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        await ValidateInstitution(model);
        if (!ModelState.IsValid) return this.StackView(model);

        paper.Title = model.Title.Trim();
        paper.InstitutionId = model.InstitutionId!.Value;
        paper.TimeLimit = model.TimeLimit;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private Task<List<Institution>> Institutions() => context.Institutions.AsNoTracking()
        .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();

    private async Task ValidateInstitution(EditViewModel model)
    {
        model.Institutions = await Institutions();
        if (model.InstitutionId.HasValue && model.Institutions.All(c => c.Id != model.InstitutionId.Value))
            ModelState.AddModelError(nameof(model.InstitutionId), localizer["Select an existing institution."]);
    }
}
