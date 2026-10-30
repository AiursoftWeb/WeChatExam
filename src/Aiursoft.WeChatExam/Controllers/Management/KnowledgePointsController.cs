using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle knowledgePoints related actions like create, edit, delete, etc.
/// </summary>
[Authorize]
[LimitPerMin]
public class KnowledgePointsController(
    WeChatExamDbContext context,
    KnowledgeExerciseValidation exerciseValidation,
    IStringLocalizer<KnowledgePointsController> localizer) : Controller
{

    // GET: knowledge-points
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Knowledge Management",
        CascadedLinksIcon = "book-open",
        CascadedLinksOrder = 20,
        LinkText = "Knowledge Points",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var knowledgePoints = await context.KnowledgePoints
            .Include(k => k.KnowledgeCategoryKnowledgePoints)
            .ThenInclude(ck => ck.KnowledgeCategory)
            .ToListAsync();

        return this.StackView(new IndexViewModel
        {
            KnowledgePoints = knowledgePoints
        });
    }

    // GET: knowledgePoints/create
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> Create()
    {
        var model = new CreateViewModel
        {
            AvailableCategories = await context.KnowledgeCategories.Include(c => c.TotalCategory).ToListAsync(),
            AvailableQuestions = new List<Question>(),
            QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            })
        };
        return this.StackView(model);
    }

    // POST: knowledgePoints
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        await ValidateKnowledgeCategories(model.SelectedKnowledgeCategoryIds);
        await ValidateQuestions(model.SelectedQuestionIds);
        var exerciseError = await exerciseValidation.ValidateAssociationsAsync(model.SelectedKnowledgeCategoryIds, model.SelectedQuestionIds);
        if (exerciseError != null) ModelState.AddModelError(nameof(model.SelectedQuestionIds), exerciseError);
        if (!ModelState.IsValid)
        {
            model.AvailableCategories = await context.KnowledgeCategories.Include(c => c.TotalCategory).ToListAsync();
            model.AvailableQuestions = await context.Questions
                .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                .ToListAsync();
            model.QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            });
            return this.StackView(model);
        }

        var knowledgePoint = new KnowledgePoint
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            Content = model.Content
        };

        context.KnowledgePoints.Add(knowledgePoint);

        // Add Category Associations
        foreach (var categoryId in model.SelectedKnowledgeCategoryIds)
        {
            var nextOrder = (await context.KnowledgeCategoryKnowledgePoints
                .Where(association => association.KnowledgeCategoryId == categoryId)
                .MaxAsync(association => (int?)association.OrderIndex) ?? -1) + 1;
            var newAssociation = new KnowledgeCategoryKnowledgePoint
            {
                KnowledgePointId = knowledgePoint.Id,
                KnowledgeCategoryId = categoryId,
                OrderIndex = nextOrder
            };
            context.Add(newAssociation);
        }

        // Add Question Associations
        foreach (var questionId in model.SelectedQuestionIds)
        {
            var newAssociation = new KnowledgePointQuestion
            {
                KnowledgePointId = knowledgePoint.Id,
                QuestionId = questionId
            };
            context.Add(newAssociation);
        }

        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = knowledgePoint.Id });
    }

    // GET: knowledgePoints/{id}
    public async Task<IActionResult> Details(Guid? id, string? returnUrl)
    {
        if (id == null) return NotFound();

        var knowledgePoint = await context.KnowledgePoints
            .Include(c => c.KnowledgeCategoryKnowledgePoints)
            .ThenInclude(ck => ck.KnowledgeCategory)
            .Include(c => c.KnowledgePointQuestions)
            .ThenInclude(kq => kq.Question)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            KnowledgePoint = knowledgePoint,
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null,
            AssociatedCategories = knowledgePoint.KnowledgeCategoryKnowledgePoints
                .Select(ck => ck.KnowledgeCategory)
                .ToList(),
            AssociatedQuestions = knowledgePoint.KnowledgePointQuestions
                .Select(kq => kq.Question)
                .ToList()
        });
    }

    // GET: knowledgePoints/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> Edit(Guid? id, string? returnUrl, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id == null) return NotFound();
        
        var knowledgePoint = await context.KnowledgePoints
            .Include(k => k.KnowledgeCategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions)
            .FirstOrDefaultAsync(k => k.Id == id);
            
        if (knowledgePoint == null) return NotFound();

        var availableCategories = await context.KnowledgeCategories.Include(c => c.TotalCategory).ToListAsync();
        var selectedQuestions = await context.Questions
            .Where(q => knowledgePoint.KnowledgePointQuestions.Select(kq => kq.QuestionId).Contains(q.Id))
            .ToListAsync();

        var model = new EditViewModel
        {
            Id = id.Value,
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null,
            Title = knowledgePoint.Title,
            Content = knowledgePoint.Content,
            AvailableCategories = availableCategories,
            AvailableQuestions = selectedQuestions,
            SelectedKnowledgeCategoryIds = knowledgePoint.KnowledgeCategoryKnowledgePoints.Select(ck => ck.KnowledgeCategoryId).ToList(),
            SelectedQuestionIds = knowledgePoint.KnowledgePointQuestions.Select(kq => kq.QuestionId).ToList(),
            QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            })
        };

        return this.StackView(model);
    }

    // POST: knowledgePoints/{id}
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id != model.Id) return NotFound();

        await ValidateKnowledgeCategories(model.SelectedKnowledgeCategoryIds);
        await ValidateQuestions(model.SelectedQuestionIds);
        var exerciseError = await exerciseValidation.ValidateAssociationsAsync(model.SelectedKnowledgeCategoryIds, model.SelectedQuestionIds);
        if (exerciseError != null) ModelState.AddModelError(nameof(model.SelectedQuestionIds), exerciseError);
        if (!ModelState.IsValid)
        {
            model.AvailableCategories = await context.KnowledgeCategories.Include(c => c.TotalCategory).ToListAsync();
            model.AvailableQuestions = await context.Questions
                .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                .ToListAsync();
            model.QuestionTypeOptions = Enum.GetValues<QuestionType>().Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()]
            });
            return this.StackView(model);
        }

        var knowledgePoint = await context.KnowledgePoints
            .Include(k => k.KnowledgeCategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions)
            .FirstOrDefaultAsync(k => k.Id == id);
            
        if (knowledgePoint == null) return NotFound();

        knowledgePoint.Title = model.Title;
        knowledgePoint.Content = model.Content;

        // Update Category Associations
        var currentCategoryIds = knowledgePoint.KnowledgeCategoryKnowledgePoints.Select(c => c.KnowledgeCategoryId).ToList();
        var newCategoryIds = model.SelectedKnowledgeCategoryIds;

        // Remove unselected
        var toRemoveCategories = knowledgePoint.KnowledgeCategoryKnowledgePoints
            .Where(ck => !newCategoryIds.Contains(ck.KnowledgeCategoryId))
            .ToList();
        
        foreach (var item in toRemoveCategories)
        {
             context.Remove(item); 
        }

        // Add new
        foreach (var categoryId in newCategoryIds)
        {
            if (!currentCategoryIds.Contains(categoryId))
            {
                var nextOrder = (await context.KnowledgeCategoryKnowledgePoints
                    .Where(association => association.KnowledgeCategoryId == categoryId)
                    .MaxAsync(association => (int?)association.OrderIndex) ?? -1) + 1;
                var newAssociation = new KnowledgeCategoryKnowledgePoint
                 {
                     KnowledgePointId = knowledgePoint.Id,
                     KnowledgeCategoryId = categoryId,
                     OrderIndex = nextOrder
                 };
                 context.Add(newAssociation);
            }
        }

        // Update Question Associations
        var currentQuestionIds = knowledgePoint.KnowledgePointQuestions.Select(q => q.QuestionId).ToList();
        var newQuestionIds = model.SelectedQuestionIds;

        // Remove unselected
        var toRemoveQuestions = knowledgePoint.KnowledgePointQuestions
            .Where(kq => !newQuestionIds.Contains(kq.QuestionId))
            .ToList();
            
        foreach(var item in toRemoveQuestions)
        {
            context.Remove(item);
        }

        // Add new
        foreach (var questionId in newQuestionIds)
        {
            if (!currentQuestionIds.Contains(questionId))
            {
                var newAssociation = new KnowledgePointQuestion
                {
                    KnowledgePointId = knowledgePoint.Id,
                    QuestionId = questionId
                };
                context.Add(newAssociation);
            }
        }

        await context.SaveChangesAsync();

        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);
        if (Url.IsLocalUrl(model.ReturnUrl)) return Redirect(model.ReturnUrl!);

        return RedirectToAction(nameof(Details), new { id = knowledgePoint.Id });
    }

    // GET: knowledgePoints/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyKnowledgePoint)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var knowledgePoint = await context.KnowledgePoints
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            KnowledgePoint = knowledgePoint
        });
    }

    // POST: knowledgePoints/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyKnowledgePoint)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid? id)
    {
        if (id == null) return NotFound();

        var knowledgePoint = await context.KnowledgePoints.FindAsync(id);
        if (knowledgePoint == null) return NotFound();

        context.KnowledgePoints.Remove(knowledgePoint);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateQuestions(List<Guid> ids)
    {
        // The editor may be stale after a question is deleted. Reject the whole selection
        // before changing the lesson or its links, just as for knowledge categories.
        if (ids.Distinct().Count() != ids.Count ||
            await context.Questions.CountAsync(q => ids.Contains(q.Id)) != ids.Count)
            ModelState.AddModelError("SelectedQuestionIds", localizer["Select existing questions without duplicates."]);
    }

    private async Task ValidateKnowledgeCategories(List<Guid> ids)
    {
        if (ids.Distinct().Count() != ids.Count ||
            await context.KnowledgeCategories.CountAsync(c => ids.Contains(c.Id)) != ids.Count)
            ModelState.AddModelError("SelectedKnowledgeCategoryIds", localizer["Select existing knowledge categories."]);
    }
}
