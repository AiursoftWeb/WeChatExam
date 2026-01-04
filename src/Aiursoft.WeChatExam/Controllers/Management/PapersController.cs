using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PapersViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// Controller for managing exam papers
/// </summary>
[LimitPerMin]
public class PapersController(TemplateDbContext context, IPaperService paperService) : Controller
{
    // GET: papers
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Papers",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var papers = await paperService.GetAllPapersAsync();
        return this.StackView(new IndexViewModel { Papers = papers });
    }

    // GET: papers/create
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    // POST: papers/create
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var paper = await paperService.CreatePaperAsync(model.Title, model.TimeLimit, model.IsFree);
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
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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
        var availableQuestions = await context.Questions.OrderByDescending(q => q.CreationTime).ToListAsync();

        return this.StackView(new EditViewModel
        {
            Id = id.Value,
            Title = paper.Title,
            TimeLimit = paper.TimeLimit,
            IsFree = paper.IsFree,
            Status = paper.Status,
            PaperQuestions = questions,
            AvailableQuestions = availableQuestions
        });
    }

    // POST: papers/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            model.AvailableQuestions = await context.Questions.ToListAsync();
            return this.StackView(model);
        }

        try
        {
            await paperService.UpdatePaperAsync(id, model.Title, model.TimeLimit, model.IsFree);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            model.PaperQuestions = await paperService.GetQuestionsForPaperAsync(id);
            model.AvailableQuestions = await context.Questions.ToListAsync();
            return this.StackView(model);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    // POST: papers/{id}/add-question
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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

    // POST: papers/{id}/remove-question
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
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
    [Authorize(Policy = AppPermissionNames.CanDeleteQuestions)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var paper = await paperService.GetPaperAsync(id.Value);
        if (paper == null) return NotFound();

        return this.StackView(new DeleteViewModel { Paper = paper });
    }

    // POST: papers/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteQuestions)]
    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        await paperService.DeletePaperAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
