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
public class MockPapersController(WeChatExamDbContext context, MockPaperValidation validation, MockPaperPublishing publishing, ILogger<MockPapersController> logger, IStringLocalizer<MockPapersController> localizer) : Controller
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
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
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
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        await ValidateCourse(model);
        if (model.TotalCategoryId.HasValue)
        {
            var error = await validation.ValidatePaperCourseAsync(id, model.TotalCategoryId.Value);
            if (error != null) ModelState.AddModelError(nameof(model.TotalCategoryId), error);
        }
        if (!ModelState.IsValid) return this.StackView(model);

        paper.Title = model.Title.Trim();
        paper.TotalCategoryId = model.TotalCategoryId!.Value;
        paper.TimeLimit = model.TimeLimit;
        paper.AllowViewAnonymousely = model.AllowViewAnonymousely;
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
        var paper = await context.MockPapers.AsNoTracking().Include(p => p.TotalCategory).SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        var periods = await context.QuestionCategories.AsNoTracking().Where(c => c.TotalCategoryId == paper.TotalCategoryId)
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();
        if (questionCategoryId.HasValue && periods.All(c => c.Id != questionCategoryId)) return BadRequest();
        search = search?.Trim();
        var selected = context.MockPaperQuestions.AsNoTracking().Where(q => q.MockPaperId == id);
        var available = context.Questions.AsNoTracking().Where(q => q.QuestionCategory!.TotalCategoryId == paper.TotalCategoryId
            && !selected.Any(l => l.QuestionId == q.Id));
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
                .OrderBy(q => q.Order).ThenBy(q => q.QuestionId)
                .Skip((page - 1) * QuestionsViewModel.PageSize).Take(QuestionsViewModel.PageSize).ToListAsync(),
            Available = !selecting ? [] : await available.Include(q => q.QuestionCategory)
                .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id)
                .Skip((page - 1) * QuestionsViewModel.PageSize).Take(QuestionsViewModel.PageSize).ToListAsync()
        }, nameof(Questions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> AddQuestions(Guid id, AddQuestionsRequest model, string? search, Guid? questionCategoryId, int page = 1)
    {
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        if (!ModelState.IsValid) return await QuestionPage(id, true, search, questionCategoryId, page);
        var ids = model.QuestionIds.Distinct().ToArray();
        var eligible = await context.Questions.Where(q => ids.Contains(q.Id) && q.QuestionCategory!.TotalCategoryId == paper.TotalCategoryId)
            .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync();
        // Validate the complete selection before writing any links, including crafted requests.
        if (eligible.Count != ids.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Every selected question must exist and belong to this paper's course."]);
            return await QuestionPage(id, true, search, questionCategoryId, page);
        }
        var existing = await context.MockPaperQuestions.Where(q => q.MockPaperId == id).ToListAsync();
        var additions = eligible.Except(existing.Select(q => q.QuestionId)).ToArray();
        var lastOrder = existing.Count == 0 ? 0 : existing.Max(q => q.Order);
        if (lastOrder > int.MaxValue - additions.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Reduce the highest question order before adding more questions."]);
            return await QuestionPage(id, true, search, questionCategoryId, page);
        }
        foreach (var questionId in additions)
            context.MockPaperQuestions.Add(new MockPaperQuestion
            {
                MockPaperId = id, QuestionId = questionId, Order = ++lastOrder, Score = model.Score
            });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(SelectQuestions), new { id, search, questionCategoryId, page });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> UpdateQuestion(Guid id, UpdateQuestionRequest model, int page = 1)
    {
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        if (!ModelState.IsValid) return await QuestionPage(id, false, null, null, page);
        var link = await context.MockPaperQuestions.SingleOrDefaultAsync(q => q.MockPaperId == id && q.QuestionId == model.QuestionId);
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
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        var link = await context.MockPaperQuestions.SingleOrDefaultAsync(q => q.MockPaperId == id && q.QuestionId == questionId);
        if (link == null) return NotFound();
        context.MockPaperQuestions.Remove(link);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Questions), new { id, page });
    }

    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> Versions(Guid id, int page = 1)
    {
        var paper = await context.MockPapers.AsNoTracking().Include(p => p.TotalCategory).SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        var query = context.PaperSnapshots.AsNoTracking().Where(s => s.MockPaperId == id);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + VersionsViewModel.PageSize - 1) / VersionsViewModel.PageSize));
        return this.StackView(new VersionsViewModel
        {
            Paper = paper, Page = page, Total = total,
            DraftQuestionCount = await context.MockPaperQuestions.CountAsync(l => l.MockPaperId == id),
            Versions = await query.OrderByDescending(s => s.Version).Skip((page - 1) * VersionsViewModel.PageSize)
                .Take(VersionsViewModel.PageSize).ToListAsync()
        }, nameof(Versions));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> Publish(Guid id)
    {
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        PaperSnapshot snapshot;
        try
        {
            snapshot = await publishing.PublishAsync(paper);
        }
        catch (InvalidOperationException error)
        {
            ModelState.AddModelError(string.Empty, error.Message);
            return await Versions(id);
        }
        catch (DbUpdateException error)
        {
            logger.LogWarning(error, "Could not publish mock paper {PaperId}", id);
            // A concurrent publication may already have used the next version number.
            return Conflict(localizer["The version could not be saved. Reload the version list before trying again."].Value);
        }
        return RedirectToAction(nameof(Version), new { id, snapshotId = snapshot.Id });
    }

    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> Version(Guid id, Guid snapshotId, int page = 1)
    {
        var paper = await context.MockPapers.AsNoTracking().Include(p => p.TotalCategory).SingleOrDefaultAsync(p => p.Id == id);
        var snapshot = await context.PaperSnapshots.AsNoTracking().SingleOrDefaultAsync(s => s.Id == snapshotId && s.MockPaperId == id);
        if (paper == null || snapshot == null) return NotFound();
        var query = context.QuestionSnapshots.AsNoTracking().Where(q => q.PaperSnapshotId == snapshotId);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + VersionsViewModel.PageSize - 1) / VersionsViewModel.PageSize));
        return this.StackView(new VersionsViewModel
        {
            Paper = paper, Snapshot = snapshot, Page = page, Total = total, TotalScore = await query.SumAsync(q => (long)q.Score),
            Questions = await query.OrderBy(q => q.Order).ThenBy(q => q.OriginalQuestionId).ThenBy(q => q.Id)
                .Skip((page - 1) * VersionsViewModel.PageSize).Take(VersionsViewModel.PageSize).ToListAsync()
        });
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
