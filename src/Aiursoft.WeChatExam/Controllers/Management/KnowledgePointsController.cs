using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle knowledgePoints related actions like create, edit, delete, etc.
/// </summary>
[Authorize]
[LimitPerMin]
public class KnowledgePointsController(WeChatExamDbContext context) : Controller
{

    // GET: knowledge-points
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1,
        LinkText = "Knowledge Points",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var knowledgePoints = await context.KnowledgePoints
            .Include(k => k.CategoryKnowledgePoints)
            .ThenInclude(ck => ck.Category)
            .ToListAsync();
        // Build a hierarchical structure
        var rootKnowledgePoints = knowledgePoints.Where(c => c.ParentId == null).ToList();

        return this.StackView(new IndexViewModel
        {
            KnowledgePoints = knowledgePoints,
            RootKnowledgePoints = rootKnowledgePoints
        });
    }

    // GET: knowledgePoints/create
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> Create()
    {
        var knowledgePoints = await context.KnowledgePoints.ToListAsync();
        var model = new CreateViewModel
        {
            AvailableParents = knowledgePoints,
            AvailableCategories = await context.Categories.ToListAsync(),
            AvailableQuestions = new List<Question>()
        };
        return this.StackView(model);
    }

    // POST: knowledgePoints
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableParents = await context.KnowledgePoints.Where(c => c.ParentId == null).ToListAsync();
            model.AvailableCategories = await context.Categories.ToListAsync();
            model.AvailableQuestions = await context.Questions
                .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                .ToListAsync();
            return this.StackView(model);
        }

        // 如果指定了父分类，验证父分类是否存在
        if (model.ParentId.HasValue)
        {
            var parentExists = await context.KnowledgePoints
                .AnyAsync(c => c.Id == model.ParentId.Value);

            if (!parentExists)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Parent knowledgePoint not found");
                model.AvailableParents = await context.KnowledgePoints.Where(c => c.ParentId == null).ToListAsync();
                model.AvailableCategories = await context.Categories.ToListAsync();
                model.AvailableQuestions = await context.Questions
                    .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                    .ToListAsync();
                return this.StackView(model);
            }
        }

        var knowledgePoint = new KnowledgePoint
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            Content = model.Content,
            ParentId = model.ParentId
        };

        context.KnowledgePoints.Add(knowledgePoint);

        // Add Category Associations
        foreach (var categoryId in model.SelectedCategoryIds)
        {
            var newAssociation = new CategoryKnowledgePoint
            {
                KnowledgePointId = knowledgePoint.Id,
                CategoryId = categoryId
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
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var knowledgePoint = await context.KnowledgePoints
            .Include(c => c.Parent)
            .Include(c => c.CategoryKnowledgePoints)
            .ThenInclude(ck => ck.Category)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (knowledgePoint == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            KnowledgePoint = knowledgePoint,
            AssociatedCategories = knowledgePoint.CategoryKnowledgePoints
                .Select(ck => ck.Category)
                .ToList()
        });
    }

    // GET: knowledgePoints/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();
        
        var knowledgePoint = await context.KnowledgePoints
            .Include(k => k.CategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions)
            .FirstOrDefaultAsync(k => k.Id == id);
            
        if (knowledgePoint == null) return NotFound();

        var availableParents = await context.KnowledgePoints
            .Where(c => c.Id != id && c.ParentId == null)
            .ToListAsync();

        var availableCategories = await context.Categories.ToListAsync();
        var selectedQuestions = await context.Questions
            .Where(q => knowledgePoint.KnowledgePointQuestions.Select(kq => kq.QuestionId).Contains(q.Id))
            .ToListAsync();

        var model = new EditViewModel
        {
            Id = id.Value,
            Title = knowledgePoint.Title,
            Content = knowledgePoint.Content,
            ParentId = knowledgePoint.ParentId,
            AvailableParents = availableParents,
            AvailableCategories = availableCategories,
            AvailableQuestions = selectedQuestions,
            SelectedCategoryIds = knowledgePoint.CategoryKnowledgePoints.Select(ck => ck.CategoryId).ToList(),
            SelectedQuestionIds = knowledgePoint.KnowledgePointQuestions.Select(kq => kq.QuestionId).ToList()
        };

        return this.StackView(model);
    }

    // POST: knowledgePoints/{id}
    [Authorize(Policy = AppPermissionNames.CanEditAnyKnowledgePoint)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.AvailableParents = await context.KnowledgePoints
                .Where(c => c.Id != id && c.ParentId == null)
                .ToListAsync();
            model.AvailableCategories = await context.Categories.ToListAsync();
            model.AvailableQuestions = await context.Questions
                .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                .ToListAsync();
            return this.StackView(model);
        }

        var knowledgePoint = await context.KnowledgePoints
            .Include(k => k.CategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions)
            .FirstOrDefaultAsync(k => k.Id == id);
            
        if (knowledgePoint == null) return NotFound();

        // 验证不能将分类设置为自己的子孙分类的父分类
        if (model.ParentId.HasValue)
        {
            if (model.ParentId.Value == id)
            {
                ModelState.AddModelError(nameof(model.ParentId), "KnowledgePoint cannot be its own parent");
                model.AvailableParents = await context.KnowledgePoints
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                model.AvailableCategories = await context.Categories.ToListAsync();
                model.AvailableQuestions = await context.Questions
                    .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                    .ToListAsync();
                return this.StackView(model);
            }

            var parentExists = await context.KnowledgePoints
                .AnyAsync(c => c.Id == model.ParentId.Value);

            if (!parentExists)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Parent knowledgePoint not found");
                model.AvailableParents = await context.KnowledgePoints
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                model.AvailableCategories = await context.Categories.ToListAsync();
                model.AvailableQuestions = await context.Questions
                    .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                    .ToListAsync();
                return this.StackView(model);
            }

            // 防止循环引用
            if (await IsDescendantOf(model.ParentId.Value, id))
            {
                ModelState.AddModelError(nameof(model.ParentId), "Cannot create circular reference");
                model.AvailableParents = await context.KnowledgePoints
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                model.AvailableCategories = await context.Categories.ToListAsync();
                model.AvailableQuestions = await context.Questions
                    .Where(q => model.SelectedQuestionIds.Contains(q.Id))
                    .ToListAsync();
                return this.StackView(model);
            }
        }

        knowledgePoint.Title = model.Title;
        knowledgePoint.Content = model.Content;
        knowledgePoint.ParentId = model.ParentId;

        // Update Category Associations
        var currentCategoryIds = knowledgePoint.CategoryKnowledgePoints.Select(c => c.CategoryId).ToList();
        var newCategoryIds = model.SelectedCategoryIds;

        // Remove unselected
        var toRemoveCategories = knowledgePoint.CategoryKnowledgePoints
            .Where(ck => !newCategoryIds.Contains(ck.CategoryId))
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
                var newAssociation = new CategoryKnowledgePoint
                 {
                     KnowledgePointId = knowledgePoint.Id,
                     CategoryId = categoryId
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

        context.KnowledgePoints.Update(knowledgePoint);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = knowledgePoint.Id });
    }

    // GET: knowledgePoints/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyKnowledgePoint)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var knowledgePoint = await context.KnowledgePoints
            .Include(c => c.Children)
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

        // Check if knowledgePoint has children
        var hasChildren = await context.KnowledgePoints.AnyAsync(c => c.ParentId == id);
        if (hasChildren)
        {
            var knowledgePointWithChildren = await context.KnowledgePoints
                .Include(c => c.Children)
                .FirstOrDefaultAsync(c => c.Id == id);

            return this.StackView(new DeleteViewModel
            {
                KnowledgePoint = knowledgePointWithChildren!,
                HasChildren = true
            });
        }

        context.KnowledgePoints.Remove(knowledgePoint);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // 辅助方法：检查 possibleAncestorId 是否是 knowledgePointId 的祖先
    private async Task<bool> IsDescendantOf(Guid possibleAncestorId, Guid knowledgePointId)
    {
        var current = await context.KnowledgePoints.FindAsync(possibleAncestorId);

        while (current?.ParentId != null)
        {
            if (current.ParentId.Value == knowledgePointId)
            {
                return true;
            }
            current = await context.KnowledgePoints.FindAsync(current.ParentId.Value);
        }

        return false;
    }
}
