using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.CategoriesViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle categories related actions like create, edit, delete, etc.
/// </summary>
[Authorize]
[LimitPerMin]
public class CategoriesController(WeChatExamDbContext context) : Controller
{

    // GET: Categories
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1,
        LinkText = "Categories",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var categories = await context.Categories
            .Include(c => c.CategoryKnowledgePoints)
            .ThenInclude(ck => ck.KnowledgePoint)
            .OrderBy(c => c.OrderIndex)
            .ToListAsync();
        // Build a hierarchical structure
        var rootCategories = categories
            .Where(c => c.ParentId == null)
            .OrderBy(c => c.OrderIndex)
            .ToList();
        
        return this.StackView(new IndexViewModel
        {
            Categories = categories,
            RootCategories = rootCategories
        });
    }

    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> UpdateOrder([FromBody] Guid[] ids)
    {
        for (var i = 0; i < ids.Length; i++)
        {
            var category = await context.Categories.FindAsync(ids[i]);
            if (category != null)
            {
                category.OrderIndex = i;
            }
        }
        await context.SaveChangesAsync();
        return Ok();
    }

    // GET: categories/create
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Create()
    {
        var categories = await context.Categories.ToListAsync();
        var model = new CreateViewModel
        {
            AvailableParents = categories
        };
        return this.StackView(model);
    }

    // POST: categories
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableParents = await context.Categories.ToListAsync();
            return this.StackView(model);
        }

        // 如果指定了父分类，验证父分类是否存在
        if (model.ParentId.HasValue)
        {
            var parentExists = await context.Categories
                .AnyAsync(c => c.Id == model.ParentId.Value);
            
            if (!parentExists)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Parent category not found");
                model.AvailableParents = await context.Categories.ToListAsync();
                return this.StackView(model);
            }
        }

        var nextOrder = await context.Categories
            .Where(c => c.ParentId == model.ParentId)
            .Select(c => (int?)c.OrderIndex)
            .MaxAsync() ?? -1;
        nextOrder++;

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            IsFree = model.IsFree,
            ParentId = model.ParentId,
            OrderIndex = nextOrder
        };

        context.Categories.Add(category);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = category.Id });
    }

    // GET: categories/{id}
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();
        
        var category = await context.Categories
            .Include(c => c.Parent)
            .Include(c => c.CategoryKnowledgePoints)
            .ThenInclude(ck => ck.KnowledgePoint)
            .FirstOrDefaultAsync(c => c.Id == id);
        
        if (category == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            Category = category,
            AssociatedKnowledgePoints = category.CategoryKnowledgePoints
                .Select(ck => ck.KnowledgePoint)
                .ToList()
        });
    }

    // GET: categories/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();
        
        var category = await context.Categories.FindAsync(id);
        if (category == null) return NotFound();

        var availableParents = await context.Categories
            .Where(c => c.Id != id)
            .ToListAsync();

        var model = new EditViewModel
        {
            Id = id.Value,
            Title = category.Title,
            IsFree = category.IsFree,
            ParentId = category.ParentId,
            AvailableParents = availableParents
        };

        return this.StackView(model);
    }

    // POST: categories/{id}
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.AvailableParents = await context.Categories
                .Where(c => c.Id != id)
                .ToListAsync();
            return this.StackView(model);
        }

        var category = await context.Categories.FindAsync(id);
        if (category == null) return NotFound();

        // 验证不能将分类设置为自己的子孙分类的父分类
        if (model.ParentId.HasValue)
        {
            if (model.ParentId.Value == id)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Category cannot be its own parent");
                model.AvailableParents = await context.Categories
                    .Where(c => c.Id != id)
                    .ToListAsync();
                return this.StackView(model);
            }

            var parentExists = await context.Categories
                .AnyAsync(c => c.Id == model.ParentId.Value);
            
            if (!parentExists)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Parent category not found");
                model.AvailableParents = await context.Categories
                    .Where(c => c.Id != id)
                    .ToListAsync();
                return this.StackView(model);
            }

            // 防止循环引用
            if (await IsDescendantOf(model.ParentId.Value, id))
            {
                ModelState.AddModelError(nameof(model.ParentId), "Cannot create circular reference");
                model.AvailableParents = await context.Categories
                    .Where(c => c.Id != id)
                    .ToListAsync();
                return this.StackView(model);
            }
        }

        if (category.ParentId != model.ParentId)
        {
            var nextOrder = await context.Categories
                .Where(c => c.ParentId == model.ParentId)
                .Select(c => (int?)c.OrderIndex)
                .MaxAsync() ?? -1;
            category.OrderIndex = nextOrder + 1;
        }

        category.Title = model.Title;
        category.IsFree = model.IsFree;
        category.ParentId = model.ParentId;

        context.Categories.Update(category);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = category.Id });
    }

    // GET: categories/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyCategory)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var category = await context.Categories
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null) return NotFound();

        var hasChildren = await context.Categories.AnyAsync(c => c.ParentId == id);
        var hasQuestions = await context.Questions.AnyAsync(q => q.CategoryId == id);

        return this.StackView(new DeleteViewModel
        {
            Category = category,
            HasChildren = hasChildren,
            HasQuestions = hasQuestions
        });
    }

    // POST: categories/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyCategory)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("Delete")]
    public async Task<IActionResult> DeleteConfirmed(Guid? id)
    {
        if (id == null) return NotFound();

        var category = await context.Categories.FindAsync(id);
        if (category == null) return NotFound();

        // Check if category has children or questions
        var hasChildren = await context.Categories.AnyAsync(c => c.ParentId == id);
        var hasQuestions = await context.Questions.AnyAsync(q => q.CategoryId == id);
        if (hasChildren || hasQuestions)
        {
            return this.StackView(new DeleteViewModel
            {
                Category = category,
                HasChildren = hasChildren,
                HasQuestions = hasQuestions
            });
        }

        context.Categories.Remove(category);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // 辅助方法：检查 possibleAncestorId 是否是 categoryId 的祖先
    private async Task<bool> IsDescendantOf(Guid possibleAncestorId, Guid categoryId)
    {
        var current = await context.Categories.FindAsync(possibleAncestorId);
        
        while (current?.ParentId != null)
        {
            if (current.ParentId.Value == categoryId)
            {
                return true;
            }
            current = await context.Categories.FindAsync(current.ParentId.Value);
        }
        
        return false;
    }
}