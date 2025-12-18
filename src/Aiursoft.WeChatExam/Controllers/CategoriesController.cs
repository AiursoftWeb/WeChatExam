using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers;

[Route("api/[controller]")]
[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly TemplateDbContext _context;

    public CategoriesController(TemplateDbContext context)
    {
        _context = context;
    }

    // GET: api/categories/all
    [HttpGet("all")]
    public async Task<IActionResult> GetCategoryAll()
    {
        var categories = await _context.Categories
            .Include(c => c.Children)
            .Where(c => c.ParentId == null)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Title = c.Title,
                Children = c.Children.Select(child => new Child
                {
                    Id = child.Id,
                    Title = child.Title
                }).ToArray()
            })
            .ToListAsync();

        return Ok(categories);
    }

    // GET: api/categories/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetCategory(Guid id)
    {
        var category = await _context.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
        {
            return NotFound(new { Message = "Category not found" });
        }

        var categoryDto = new CategoryDto
        {
            Id = category.Id,
            Title = category.Title,
            Children = category.Children.Select(child => new Child
            {
                Id = child.Id,
                Title = child.Title
            }).ToArray()
        };

        return Ok(categoryDto);
    }

    // POST: api/categories
    [AdminOnly]
    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryDto model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // 如果指定了父分类，验证父分类是否存在
        if (model.ParentId.HasValue)
        {
            var parentExists = await _context.Categories
                .AnyAsync(c => c.Id == model.ParentId.Value);
            
            if (!parentExists)
            {
                return BadRequest(new { Message = "Parent category not found" });
            }
        }

        // 独裁模式：通过 DbContext.Add() 创建
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            ParentId = model.ParentId  // required 字段，必须显式赋值（即使是 null）
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, new
        {
            category.Id,
            category.Title,
            category.ParentId
        });
    }

    // PUT: api/categories/{id}
    [AdminOnly]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] UpdateCategoryDto model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound(new { Message = "Category not found" });
        }

        // 如果指定了父分类，验证父分类是否存在且不是自己
        if (model.ParentId.HasValue)
        {
            if (model.ParentId.Value == id)
            {
                return BadRequest(new { Message = "Category cannot be its own parent" });
            }

            var parentExists = await _context.Categories
                .AnyAsync(c => c.Id == model.ParentId.Value);
            
            if (!parentExists)
            {
                return BadRequest(new { Message = "Parent category not found" });
            }

            // 防止循环引用：检查父分类不是当前分类的子孙分类
            if (await IsDescendantOf(model.ParentId.Value, id))
            {
                return BadRequest(new { Message = "Cannot create circular reference" });
            }
        }

        category.Title = model.Title;
        category.ParentId = model.ParentId;

        _context.Categories.Update(category);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Category updated successfully", category.Id });
    }

    // DELETE: api/categories/{id}
    [AdminOnly]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(Guid id)
    {
        // 显式加载子分类集合
        var category = await _context.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
        {
            return NotFound(new { Message = "Category not found" });
        }

        // 独裁模式：检查集合但不能通过集合操作
        // 由于集合是 IEnumerable，无法 .Add() 或 .Remove()
        if (category.Children.Any())
        {
            return BadRequest(new { Message = "Cannot delete category with children. Please delete children first." });
        }

        // 独裁模式：通过 DbContext.Remove() 删除
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Category deleted successfully" });
    }

    // 辅助方法：检查 possibleAncestorId 是否是 categoryId 的祖先
    private async Task<bool> IsDescendantOf(Guid possibleAncestorId, Guid categoryId)
    {
        var current = await _context.Categories.FindAsync(possibleAncestorId);
        
        while (current?.ParentId != null)
        {
            if (current.ParentId.Value == categoryId)
            {
                return true;
            }
            current = await _context.Categories.FindAsync(current.ParentId.Value);
        }
        
        return false;
    }
}