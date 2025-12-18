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
[LimitPerMin]
public class CategoriesController(TemplateDbContext context) : Controller
{

    // GET: categories
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Categories",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var categories = await context.Categories.ToListAsync();
        // Build a hierarchical structure
        var rootCategories = categories.Where(c => c.ParentId == null).ToList();
        
        return this.StackView(new IndexViewModel
        {
            Categories = categories,
            RootCategories = rootCategories
        });
    }

    // GET: categories/create
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Create()
    {
        var categories = await context.Categories.ToListAsync();
        var model = new CreateViewModel
        {
            AvailableParents = categories.Where(c => c.ParentId == null).ToList()
        };
        return this.StackView(model);
    }

    // POST: categories
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    // POST: categories
    [Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableParents = await context.Categories.Where(c => c.ParentId == null).ToListAsync();
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
                model.AvailableParents = await context.Categories.Where(c => c.ParentId == null).ToListAsync();
                return this.StackView(model);
            }
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            ParentId = model.ParentId
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
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);
        
        if (category == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            Category = category
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
            .Where(c => c.Id != id && c.ParentId == null)
            .ToListAsync();

        var model = new EditViewModel
        {
            Id = id.Value,
            Title = category.Title,
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
                .Where(c => c.Id != id && c.ParentId == null)
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
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                return this.StackView(model);
            }

            var parentExists = await context.Categories
                .AnyAsync(c => c.Id == model.ParentId.Value);
            
            if (!parentExists)
            {
                ModelState.AddModelError(nameof(model.ParentId), "Parent category not found");
                model.AvailableParents = await context.Categories
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                return this.StackView(model);
            }

            // 防止循环引用
            if (await IsDescendantOf(model.ParentId.Value, id))
            {
                ModelState.AddModelError(nameof(model.ParentId), "Cannot create circular reference");
                model.AvailableParents = await context.Categories
                    .Where(c => c.Id != id && c.ParentId == null)
                    .ToListAsync();
                return this.StackView(model);
            }
        }

        category.Title = model.Title;
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
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            Category = category
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

        // Check if category has children
        var hasChildren = await context.Categories.AnyAsync(c => c.ParentId == id);
        if (hasChildren)
        {
            var categoryWithChildren = await context.Categories
                .Include(c => c.Children)
                .FirstOrDefaultAsync(c => c.Id == id);
            
            return this.StackView(new DeleteViewModel
            {
                Category = categoryWithChildren!,
                HasChildren = true
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