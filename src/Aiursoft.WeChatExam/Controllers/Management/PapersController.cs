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

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// Controller for managing exam papers
/// </summary>
[LimitPerMin]
public class PapersController(WeChatExamDbContext context, IPaperService paperService) : Controller
{
    // GET: papers
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "clipboard-list",
        CascadedLinksOrder = 2,
        LinkText = "Papers",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var papers = await paperService.GetAllPapersAsync();
        return this.StackView(new IndexViewModel { Papers = papers });
    }

    // GET: papers/create
    [Authorize(Policy = AppPermissionNames.CanAddPapers)]
    public async Task<IActionResult> Create()
    {
        var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
        var model = new CreateViewModel
        {
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
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title
            });
            return this.StackView(model);
        }

        var paper = await paperService.CreatePaperAsync(model.Title, model.TimeLimit, model.IsFree);
        if (model.SelectedCategoryId.HasValue)
        {
            await paperService.AssociateCategoryAsync(paper.Id, model.SelectedCategoryId.Value);
        }
        return RedirectToAction(nameof(Edit), new { id = paper.Id });
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
    public async Task<IActionResult> Edit(Guid? id)
    {
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

        var availableQuestionsQuery = context.Questions.AsQueryable();
        if (selectedCategoryId.HasValue)
        {
            availableQuestionsQuery = availableQuestionsQuery.Where(q => q.CategoryId == selectedCategoryId.Value);
        }
        var availableQuestions = await availableQuestionsQuery.OrderByDescending(q => q.CreationTime).ToListAsync();

        var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();

        return this.StackView(new EditViewModel
        {
            Id = id.Value,
            Title = paper.Title,
            TimeLimit = paper.TimeLimit,
            IsFree = paper.IsFree,
            Status = paper.Status,
            PaperQuestions = questions,
            AvailableQuestions = availableQuestions,
            SelectedCategoryId = selectedCategoryId,
            AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == selectedCategoryId
            })
        });
    }

    // POST: papers/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditPapers)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            
             var availableQuestionsQuery = context.Questions.AsQueryable();
            if (model.SelectedCategoryId.HasValue)
            {
                availableQuestionsQuery = availableQuestionsQuery.Where(q => q.CategoryId == model.SelectedCategoryId.Value);
            }
            model.AvailableQuestions = await availableQuestionsQuery.OrderByDescending(q => q.CreationTime).ToListAsync();

            var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == model.SelectedCategoryId
            });
            
            return this.StackView(model);
        }

        try
        {
            await paperService.UpdatePaperAsync(id, model.Title, model.TimeLimit, model.IsFree);
            
            await paperService.ClearCategoriesForPaperAsync(id);
            if (model.SelectedCategoryId.HasValue)
            {
                await paperService.AssociateCategoryAsync(id, model.SelectedCategoryId.Value);
            }
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            // Re-fetch logic similar to above
            var availableQuestionsQuery = context.Questions.AsQueryable();
            if (model.SelectedCategoryId.HasValue)
            {
                availableQuestionsQuery = availableQuestionsQuery.Where(q => q.CategoryId == model.SelectedCategoryId.Value);
            }
            model.AvailableQuestions = await availableQuestionsQuery.OrderByDescending(q => q.CreationTime).ToListAsync();

            var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
            model.AvailableCategories = categories.Select(c => new SelectListItem
            {
                Value = c.Id.ToString(),
                Text = c.Title,
                Selected = c.Id == model.SelectedCategoryId
            });
            return this.StackView(model);
        }

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
    public async Task<IActionResult> SearchQuestions(Guid id, string mtql)
    {
        var query = context.Questions.AsQueryable();

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
                q.QuestionType
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
}
