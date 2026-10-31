using System.Data;
using System.Security.Cryptography;
using System.Text;
using Aiursoft.WeChatExam.Models;
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
        CascadedLinksGroupName = "Mock Paper Management",
        CascadedLinksIcon = "notebook-pen",
        CascadedLinksOrder = 40,
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
    public Task<IActionResult> SelectQuestions(Guid id, string? search, Guid? questionCategoryId, int page = 1, string? tag = null, QuestionType? questionType = null) =>
        ModelState.IsValid ? QuestionPage(id, true, search, questionCategoryId, page, tag, questionType) : Task.FromResult<IActionResult>(BadRequest());

    private async Task<IActionResult> QuestionPage(Guid id, bool selecting, string? search, Guid? questionCategoryId, int page, string? tag = null, QuestionType? questionType = null, bool reviewingMatches = false, int score = 10)
    {
        var paper = await context.MockPapers.AsNoTracking().Include(p => p.TotalCategory).SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        var periods = await context.QuestionCategories.AsNoTracking().Where(c => c.TotalCategoryId == paper.TotalCategoryId)
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();
        if (questionCategoryId.HasValue && periods.All(c => c.Id != questionCategoryId)) return BadRequest();
        if (questionType.HasValue && !Enum.IsDefined(questionType.Value)) return BadRequest();
        search = search?.Trim();
        tag = tag?.Trim();
        var selected = context.MockPaperQuestions.AsNoTracking().Where(q => q.MockPaperId == id);
        var available = MatchingQuestions(paper, search, questionCategoryId, tag, questionType);
        var matchingIds = reviewingMatches ? await available.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync() : null;
        var count = await selected.CountAsync();
        var total = selecting ? matchingIds?.Count ?? await available.CountAsync() : count;
        page = Math.Clamp(page, 1, Math.Max(1, (total + QuestionsViewModel.PageSize - 1) / QuestionsViewModel.PageSize));
        return this.StackView(new QuestionsViewModel
        {
            Paper = paper, Selecting = selecting, Search = search, QuestionCategoryId = questionCategoryId,
            Tag = tag, QuestionType = questionType, ReviewingMatches = reviewingMatches, Score = score,
            MatchFingerprint = matchingIds == null ? null : Fingerprint(matchingIds),
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

    private IQueryable<Question> MatchingQuestions(MockPaper paper, string? search, Guid? questionCategoryId, string? tag, QuestionType? questionType)
    {
        var selected = context.MockPaperQuestions.Where(q => q.MockPaperId == paper.Id);
        var available = context.Questions.AsNoTracking().Where(q => q.QuestionCategory!.TotalCategoryId == paper.TotalCategoryId && !selected.Any(l => l.QuestionId == q.Id));
        search = search?.Trim();
        tag = tag?.Trim();
        if (questionCategoryId.HasValue) available = available.Where(q => q.QuestionCategoryId == questionCategoryId);
        if (!string.IsNullOrEmpty(search)) available = available.Where(q => q.Content.Contains(search));
        if (questionType.HasValue) available = available.Where(q => q.QuestionType == questionType);
        if (!string.IsNullOrEmpty(tag))
        {
            var normalizedTag = tag.ToUpperInvariant();
            available = available.Where(q => q.QuestionTags.Any(qt => qt.Tag.NormalizedName == normalizedTag));
        }
        return available;
    }

    // This is a stale-results check, not authorization: every write re-applies the paper's eligibility rules.
    private static string Fingerprint(IEnumerable<Guid> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", ids))));

    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public Task<IActionResult> ReviewMatches(Guid id, string? search, Guid? questionCategoryId, int page = 1, string? tag = null, QuestionType? questionType = null) =>
        ModelState.IsValid ? QuestionPage(id, true, search, questionCategoryId, page, tag, questionType, true) : Task.FromResult<IActionResult>(BadRequest());

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> AddMatchingQuestions(Guid id, AddMatchingQuestionsRequest model, string? search, Guid? questionCategoryId, int page = 1, string? tag = null, QuestionType? questionType = null)
    {
        if (!ModelState.IsValid) return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType, true, model.Score);
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                // Keep the paper's editable state, course and matching set stable until this batch commits.
                // Retrying a lost commit acknowledgement re-reads links, so it cannot duplicate the batch.
                await using var transaction = context.Database.IsRelational()
                    ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
                var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
                if (paper == null) return (IActionResult)NotFound();
                if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
                var ids = await MatchingQuestions(paper, search, questionCategoryId, tag, questionType)
                    .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync();
                if (ids.Count == 0)
                {
                    TempData["SuccessMessage"] = localizer["No additional matching exam questions to add."].Value;
                    return RedirectToAction(nameof(Questions), new { id });
                }
                if (Fingerprint(ids) != model.MatchFingerprint)
                {
                    ModelState.AddModelError(string.Empty, localizer["The matching exam questions changed. Review the current results before confirming again."]);
                    return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType, true, model.Score);
                }
                var lastOrder = await context.MockPaperQuestions.Where(q => q.MockPaperId == id).MaxAsync(q => (int?)q.Order) ?? 0;
                if (lastOrder > int.MaxValue - ids.Count)
                {
                    ModelState.AddModelError(string.Empty, localizer["Reduce the highest question order before adding more questions."]);
                    return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType, true, model.Score);
                }
                context.MockPaperQuestions.AddRange(ids.Select((questionId, i) => new MockPaperQuestion
                {
                    MockPaperId = id, QuestionId = questionId, Order = lastOrder + i + 1, Score = model.Score
                }));
                // A single atomic save adds the whole set, independent of the 20-row display page.
                await context.SaveChangesAsync();
                if (transaction != null) await transaction.CommitAsync();
                TempData["SuccessMessage"] = string.Format(localizer["Added {0} matching questions."].Value, ids.Count);
                return RedirectToAction(nameof(Questions), new { id });
            });
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Could not confirm matching question additions for paper {PaperId}.", id);
            context.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, localizer["Could not confirm the batch was added. Review the current questions before trying again."]);
            return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType, true, model.Score);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> AddQuestions(Guid id, AddQuestionsRequest model, string? search, Guid? questionCategoryId, int page = 1, string? tag = null, QuestionType? questionType = null)
    {
        var paper = await context.MockPapers.SingleOrDefaultAsync(p => p.Id == id);
        if (paper == null) return NotFound();
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable)) return Conflict();
        if (!ModelState.IsValid) return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType);
        var ids = model.QuestionIds.Distinct().ToArray();
        var eligible = await context.Questions.Where(q => ids.Contains(q.Id) && q.QuestionCategory!.TotalCategoryId == paper.TotalCategoryId)
            .OrderBy(q => q.OrderIndex).ThenBy(q => q.Id).Select(q => q.Id).ToListAsync();
        // Validate the complete selection before writing any links, including crafted requests.
        if (eligible.Count != ids.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Every selected question must exist and belong to this paper's course."]);
            return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType);
        }
        var existing = await context.MockPaperQuestions.Where(q => q.MockPaperId == id).ToListAsync();
        var additions = eligible.Except(existing.Select(q => q.QuestionId)).ToArray();
        var lastOrder = existing.Count == 0 ? 0 : existing.Max(q => q.Order);
        if (lastOrder > int.MaxValue - additions.Length)
        {
            ModelState.AddModelError(string.Empty, localizer["Reduce the highest question order before adding more questions."]);
            return await QuestionPage(id, true, search, questionCategoryId, page, tag, questionType);
        }
        foreach (var questionId in additions)
            context.MockPaperQuestions.Add(new MockPaperQuestion
            {
                MockPaperId = id, QuestionId = questionId, Order = ++lastOrder, Score = model.Score
            });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(SelectQuestions), new { id, search, questionCategoryId, page, tag, questionType });
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
