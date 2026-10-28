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

    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public Task<IActionResult> Questions(Guid id, int page = 1) => QuestionPage(id, false, null, null, page);

    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public Task<IActionResult> SelectQuestions(Guid id, string? search, Guid? questionCategoryId, int page = 1) =>
        QuestionPage(id, true, search, questionCategoryId, page);

    private async Task<IActionResult> QuestionPage(Guid id, bool selecting, string? search, Guid? questionCategoryId, int page)
    {
        var paper = await context.RealPapers.AsNoTracking().Include(p => p.Institution).SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        var periods = await context.QuestionCategories.AsNoTracking().Include(c => c.TotalCategory)
            .OrderBy(c => c.TotalCategory!.OrderIndex).ThenBy(c => c.TotalCategory!.Title).ThenBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();
        if (questionCategoryId.HasValue && periods.All(c => c.Id != questionCategoryId)) return BadRequest();
        search = search?.Trim();
        var selected = context.RealPaperQuestions.AsNoTracking().Where(q => q.RealPaperId == id);
        var available = context.Questions.AsNoTracking().Where(q => !selected.Any(l => l.QuestionId == q.Id));
        if (questionCategoryId.HasValue) available = available.Where(q => q.QuestionCategoryId == questionCategoryId);
        if (!string.IsNullOrEmpty(search)) available = available.Where(q => q.Content.Contains(search));
        var count = await selected.CountAsync();
        var total = selecting ? await available.CountAsync() : count;
        page = Math.Clamp(page, 1, Math.Max(1, (total + QuestionsViewModel.PageSize - 1) / QuestionsViewModel.PageSize));
        return this.StackView(new QuestionsViewModel
        {
            Paper = paper, Selecting = selecting, Search = search, QuestionCategoryId = questionCategoryId,
            Page = page, Total = total, SelectedCount = count, TotalScore = await selected.SumAsync(q => (long)q.Score),
            Periods = periods,
            Selected = selecting ? [] : await selected.Include(q => q.Question).ThenInclude(q => q.QuestionCategory)
                .ThenInclude(c => c!.TotalCategory)
                .OrderBy(q => q.Order).ThenBy(q => q.QuestionId)
                .Skip((page - 1) * QuestionsViewModel.PageSize).Take(QuestionsViewModel.PageSize).ToListAsync(),
            Available = !selecting ? [] : await available.Include(q => q.QuestionCategory).ThenInclude(c => c!.TotalCategory)
                .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id)
                .Skip((page - 1) * QuestionsViewModel.PageSize).Take(QuestionsViewModel.PageSize).ToListAsync()
        }, nameof(Questions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> AddQuestions(Guid id, AddQuestionsRequest model, string? search, Guid? questionCategoryId, int page = 1)
    {
        var paper = await context.RealPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        if (!ModelState.IsValid) return await QuestionPage(id, true, search, questionCategoryId, page);
        var ids = model.QuestionIds.Distinct().ToArray();
        // Institution papers may span courses; selection must not impose mock-paper course ownership.
        var eligible = await context.Questions.Where(q => ids.Contains(q.Id))
            .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync();
        // Validate the complete selection before writing any links, including crafted requests.
        if (eligible.Count != ids.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Every selected question must exist."]);
            return await QuestionPage(id, true, search, questionCategoryId, page);
        }
        var existing = await context.RealPaperQuestions.Where(q => q.RealPaperId == id).ToListAsync();
        var additions = eligible.Except(existing.Select(q => q.QuestionId)).ToArray();
        var lastOrder = existing.Count == 0 ? 0 : existing.Max(q => q.Order);
        if (lastOrder > int.MaxValue - additions.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Reduce the highest question order before adding more questions."]);
            return await QuestionPage(id, true, search, questionCategoryId, page);
        }
        foreach (var questionId in additions)
            context.RealPaperQuestions.Add(new RealPaperQuestion
            {
                RealPaperId = id, QuestionId = questionId, Order = ++lastOrder, Score = model.Score
            });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(SelectQuestions), new { id, search, questionCategoryId, page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> UpdateQuestion(Guid id, UpdateQuestionRequest model, int page = 1)
    {
        var paper = await context.RealPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        if (!ModelState.IsValid) return await QuestionPage(id, false, null, null, page);
        var link = await context.RealPaperQuestions.SingleOrDefaultAsync(q => q.RealPaperId == id && q.QuestionId == model.QuestionId);
        if (link == null) return NotFound();
        link.Order = model.Order;
        link.Score = model.Score;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Questions), new { id, page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> RemoveQuestion(Guid id, Guid questionId, int page = 1)
    {
        var paper = await context.RealPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status != PaperStatus.Draft) return Conflict();
        var link = await context.RealPaperQuestions.SingleOrDefaultAsync(q => q.RealPaperId == id && q.QuestionId == questionId);
        if (link == null) return NotFound();
        context.RealPaperQuestions.Remove(link);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Questions), new { id, page });
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
