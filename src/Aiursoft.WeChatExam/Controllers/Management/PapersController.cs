using Aiursoft.Canon.TaskQueue;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PapersViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// Controller for managing exam papers
/// </summary>
[Authorize]
[LimitPerMin]
public class PapersController(
    WeChatExamDbContext context, 
    IPaperService paperService,
    ITagService tagService,
    PaperCollectionService collections,
    PaperImportService importService,
    PaperImportTasks importTasks,
    ServiceTaskQueue taskQueue,
    IStringLocalizer<PapersController> localizer) : Controller
{
    private string ImportOwner => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("Missing user identity.");

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public async Task<IActionResult> Import()
    {
        return this.StackView(new ImportViewModel
        {
            Collections = await CollectionOptionsAsync([]),
            Categories = await context.Categories.OrderBy(c => c.Title).Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToListAsync(),
            Taxonomies = await context.Taxonomies.OrderBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync()
        });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(1100000)]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public IActionResult ImportExtract([FromBody] PaperImportExtractionRequest? request)
    {
        if (request == null || !ModelState.IsValid || string.IsNullOrWhiteSpace(request.Source)) return BadRequest("Invalid source text (maximum 100000 characters).");
        PaperImportTask task;
        try { task = importTasks.Create(ImportOwner); }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        taskQueue.QueueWithDependency<IServiceProvider>("Paper-Import", $"Extract paper {task.Id}", async provider =>
        {
            task.Status = "Processing";
            try
            {
                using var scope = provider.CreateScope();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                task.Preview = await scope.ServiceProvider.GetRequiredService<PaperImportService>().ExtractAsync(request.Source, timeout.Token);
                task.Status = "Completed";
            }
            catch (Exception exception)
            {
                provider.GetRequiredService<ILogger<PapersController>>().LogWarning(exception, "Paper import extraction {TaskId} failed", task.Id);
                task.Error = "Extraction failed, timed out, or returned invalid content. Check the source and explicitly extract again.";
                task.Status = "Failed";
            }
        });
        return Json(new { taskId = task.Id });
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public IActionResult ImportStatus(Guid id)
    {
        var task = importTasks.Get(id, ImportOwner);
        return task == null ? NotFound("Preview expired or server restarted. Please extract again.") : Json(new { task.Status, task.Error, task.Preview });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(1100000)]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public async Task<IActionResult> ImportCheckTags([FromBody] PaperImportTagCheckRequest? request)
    {
        if (request == null || !ModelState.IsValid) return BadRequest("Invalid tags.");
        try { return Json(await importService.CheckTagsAsync(request)); }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(3000000)]
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public async Task<IActionResult> ImportSave([FromBody] PaperImportSaveRequest? request)
    {
        if (request == null || !ModelState.IsValid) return BadRequest("Invalid paper data. Check all field lengths and scores.");
        var task = importTasks.Get(request.TaskId, ImportOwner);
        if (task == null) return NotFound("Preview expired or server restarted. Please extract again.");
        await task.SaveLock.WaitAsync();
        try
        {
            if (task.PaperId.HasValue) return Json(new { paperId = task.PaperId, url = Url.Action(nameof(Details), new { id = task.PaperId }) });
            if (task.Status != "Completed") return BadRequest("Extract and review a valid preview before saving.");
            task.PaperId = await importService.SaveAsync(request);
            return Json(new { paperId = task.PaperId, url = Url.Action(nameof(Details), new { id = task.PaperId }) });
        }
        catch (InvalidOperationException e) { return BadRequest(e.Message); }
        finally { task.SaveLock.Release(); }
    }

    // GET: papers
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "clipboard-list",
        CascadedLinksOrder = 2,
        LinkText = "Papers",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(int? tagId)
    {
        var papers = await paperService.GetAllPapersAsync();
        if (tagId.HasValue)
        {
            var ids = await context.PaperTags.Where(t => t.TagId == tagId).Select(t => t.PaperId).ToListAsync();
            papers = papers.Where(p => ids.Contains(p.Id)).ToList();
        }
        return this.StackView(new IndexViewModel { Papers = papers });
    }

    // GET: papers/create
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create()
    {
        var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
        var model = new CreateViewModel
        {
            AvailableCollections = await CollectionOptionsAsync([]),
            AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title
            })
        };
        return this.StackView(model);
    }

    // POST: papers/create
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
            model.AvailableCollections = await CollectionOptionsAsync(model.SelectedCollectionIds);
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title
            });
            return this.StackView(model);
        }

        var paperId = Guid.NewGuid();
        try
        {
            await collections.ExecuteAtomicAsync(async () =>
            {
                var paper = await paperService.CreatePaperAsync(model.Title, model.TimeLimit, model.IsFree, model.IsRealExam);
                if (model.SelectedCategoryId.HasValue)
                {
                    await paperService.AssociateCategoryAsync(paper.Id, model.SelectedCategoryId.Value);
                }

                if (!string.IsNullOrWhiteSpace(model.Tags))
                {
                    var tags = model.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tagDisplayName in tags)
                    {
                        var tag = await tagService.GetOrCreateTagAsync(tagDisplayName);
                        await tagService.AddTagToPaperAsync(paper.Id, tag.Id);
                    }
                }

                if (model.CollectionsSubmitted) await collections.StagePaperCollectionsAsync(paper.Id, model.SelectedCollectionIds);
                await context.SaveChangesAsync();
                paperId = paper.Id;
            });
        }
        catch (Exception e) when (e is InvalidOperationException or DbUpdateException)
        {
            context.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, e is DbUpdateException ? localizer["The directory changed while saving. Reload and try again."].Value : e.Message);
            model.AvailableCollections = await CollectionOptionsAsync(model.SelectedCollectionIds);
            model.AvailableCategories = await context.Categories.OrderBy(c => c.Title).Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToListAsync();
            return this.StackView(model);
        }
        return RedirectToAction(nameof(Edit), new { id = paperId });
    }

    // GET: papers/{id}
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var paper = await paperService.GetPaperAsync(id.Value);
        if (paper == null) return NotFound();

        var questions = await paperService.GetQuestionsForPaperAsync(id.Value);
        var snapshots = await paperService.GetSnapshotsForPaperAsync(id.Value);

        return this.StackView(new DetailsViewModel
        {
            Paper = paper,
            PaperQuestions = questions,
            Snapshots = snapshots
        });
    }

    // GET: papers/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    public async Task<IActionResult> Edit(Guid? id, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id == null) return NotFound();

        var paper = await paperService.GetPaperAsync(id.Value);
        if (paper == null) return NotFound();
        if (paper.Status == PaperStatus.Frozen)
        {
            return RedirectToAction(nameof(Details), new { id });
        }

        var questions = await paperService.GetQuestionsForPaperAsync(id.Value);
        
        var associatedCategories = await paperService.GetCategoriesForPaperAsync(id.Value);
        var selectedCategoryId = associatedCategories.FirstOrDefault()?.Id;

        var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
        var tags = await tagService.GetTagsForPaperAsync(id.Value);

        var mappedTagIds = await context.LegacyPaperCollectionBindings.Select(b => b.TagId).ToListAsync();
        tags = tags.Where(t => !mappedTagIds.Contains(t.Id)).ToList();
        var selectedCollections = await context.PaperCollectionPapers.Where(p => p.PaperId == id).Select(p => p.CollectionId).ToListAsync();
        return this.StackView(new EditViewModel
        {
            SelectedCollectionIds = selectedCollections,
            CollectionRevision = await collections.MembershipRevisionAsync(id.Value),
            AvailableCollections = await CollectionOptionsAsync(selectedCollections),
            Id = id.Value,
            Title = paper.Title,
            TimeLimit = paper.TimeLimit,
            IsFree = paper.IsFree,
            IsRealExam = paper.IsRealExam,
            Status = paper.Status,
            PaperQuestions = questions,
            SelectedCategoryId = selectedCategoryId,
            Tags = string.Join(' ', tags.Select(t => t.DisplayName)),
            AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == selectedCategoryId
            }),
            QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            })
        });
    }

    // POST: papers/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            
            var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
            model.AvailableCollections = await CollectionOptionsAsync(model.SelectedCollectionIds);
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == model.SelectedCategoryId
            });
            model.QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            });
            
            return this.StackView(model);
        }

        try
        {
            await collections.ExecuteAtomicAsync(async () =>
            {
                if (model.CollectionsSubmitted) await collections.VerifyMembershipRevisionAsync(id, model.CollectionRevision);
                await paperService.UpdatePaperAsync(id, model.Title, model.TimeLimit, model.IsFree, model.IsRealExam);

                await paperService.ClearCategoriesForPaperAsync(id);
                if (model.SelectedCategoryId.HasValue)
                {
                    await paperService.AssociateCategoryAsync(id, model.SelectedCategoryId.Value);
                }

                // Sync tags
                var mappedTagIds = await context.LegacyPaperCollectionBindings.Select(b => b.TagId).ToListAsync();
                var currentTags = (await tagService.GetTagsForPaperAsync(id)).Where(t => !mappedTagIds.Contains(t.Id)).ToList();
                var submittedTags = (model.Tags?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>()).ToList();

                var mappedNames = await context.Tags.Where(t => mappedTagIds.Contains(t.Id)).Select(t => t.DisplayName).ToListAsync();
                submittedTags.RemoveAll(t => mappedNames.Contains(t));

                // Add new tags
                foreach (var tagDisplayName in submittedTags.Where(st => currentTags.All(ct => ct.DisplayName != st)))
                {
                    var tag = await tagService.GetOrCreateTagAsync(tagDisplayName);
                    await tagService.AddTagToPaperAsync(id, tag.Id);
                }

                // Remove old tags
                foreach (var tagToRemove in currentTags.Where(ct => submittedTags.All(st => st != ct.DisplayName)))
                {
                    await tagService.RemoveTagFromPaperAsync(id, tagToRemove.Id);
                }
                if (model.CollectionsSubmitted) await collections.StagePaperCollectionsAsync(id, model.SelectedCollectionIds);
                await context.SaveChangesAsync();
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
        {
            context.ChangeTracker.Clear();
            ModelState.AddModelError("", ex is DbUpdateException ? localizer["The directory changed while saving. Reload and try again."].Value : ex.Message);
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            
            var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
            model.AvailableCollections = await CollectionOptionsAsync(model.SelectedCollectionIds);
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == model.SelectedCategoryId
            });
            model.QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            });
            return this.StackView(model);
        }

        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);

        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: papers/{id}/add-question
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddQuestion(Guid id, Guid questionId, int order, int score)
    {
        try
        {
            await paperService.AddQuestionToPaperAsync(id, questionId, order, score);
        }
        catch (InvalidOperationException)
        {
            // Question already exists or paper frozen
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    // GET: papers/{id}/search-questions
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    public async Task<IActionResult> SearchQuestions(Guid id, string? mtql, QuestionType? questionType)
    {
        var query = context.Questions.AsQueryable();

        if (questionType.HasValue)
        {
            query = query.Where(q => q.QuestionType == questionType.Value);
        }

        if (!string.IsNullOrWhiteSpace(mtql))
        {
            try
            {
                var tokens = MTQL.Services.Tokenizer.Tokenize(mtql);
                var rpn = MTQL.Services.Parser.ToRpn(tokens);
                var ast = MTQL.Services.AstBuilder.Build(rpn);
                var predicate = MTQL.Services.PredicateBuilder.Build(ast);
                query = query.Where(predicate);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Invalid MTQL: {ex.Message}" });
            }
        }

        var paperQuestions = await paperService.GetQuestionsForPaperAsync(id);
        var existingQuestionIds = paperQuestions.Select(pq => pq.QuestionId).ToList();

        var questions = await query
            .Where(q => !existingQuestionIds.Contains(q.Id))
            .OrderByDescending(q => q.CreationTime)
            .Take(100)
            .Select(q => new
            {
                q.Id,
                Content = q.Content.Length > 100 ? q.Content.Substring(0, 100) + "..." : q.Content,
                QuestionType = q.QuestionType.ToString()
            })
            .ToListAsync();

        return Json(questions);
    }

    // POST: papers/{id}/batch-add-questions
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchAddQuestions(Guid id, [FromForm] Guid[]? questionIds, int startingOrder, int defaultScore)
    {
        if (questionIds == null || !questionIds.Any())
        {
            TempData["Error"] = "No questions selected.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        try
        {
            await paperService.AddQuestionsToPaperAsync(id, questionIds, startingOrder, defaultScore);
            return RedirectToAction(nameof(Edit), new { id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Edit), new { id });
        }
    }

    // POST: papers/{id}/remove-question
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveQuestion(Guid id, Guid questionId)
    {
        try
        {
            await paperService.RemoveQuestionFromPaperAsync(id, questionId);
        }
        catch (InvalidOperationException)
        {
            // Paper frozen
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    // POST: papers/{id}/set-status
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(Guid id, PaperStatus status)
    {
        try
        {
            await paperService.SetStatusAsync(id, status);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Edit), new { id });
    }

    // POST: papers/{id}/publish
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(Guid id)
    {
        try
        {
            await paperService.PublishAsync(id);
            TempData["Success"] = "Paper published successfully!";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: papers/{id}/freeze
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Freeze(Guid id)
    {
        try
        {
            await paperService.FreezeAsync(id);
            TempData["Success"] = "Paper frozen successfully!";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // GET: papers/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeletePapers)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var paper = await paperService.GetPaperAsync(id.Value);
        if (paper == null) return NotFound();

        return this.StackView(new DeleteViewModel { Paper = paper });
    }

    // POST: papers/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeletePapers)]
    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        await paperService.DeletePaperAsync(id);
        return RedirectToAction(nameof(Index));
    }

    private async Task<List<SelectListItem>> CollectionOptionsAsync(IEnumerable<Guid> selected)
    {
        var ids = selected.ToArray();
        return await context.PaperCollections.AsNoTracking().Where(c => !c.IsArchived || ids.Contains(c.Id))
            .OrderBy(c => c.OrderIndex).ThenBy(c => c.Name)
            .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name, Selected = ids.Contains(c.Id) }).ToListAsync();
    }
}
