using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Microsoft.AspNetCore.Authorization;
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
    public async Task<IActionResult> GetCategory(string id)
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
            Children = category.Children?.Select(child => new Child
            {
                Id = child.Id,
                Title = child.Title
            }).ToArray() ?? Array.Empty<Child>()
        };

        return Ok(categoryDto);
    }

    // POST: api/categories
    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryDto model)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var category = new Category
        {
            Id = Guid.NewGuid().ToString(),
            Title = model.Title,
            ParentId = model.ParentId,
            CreationTime = DateTime.UtcNow
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
    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(string id, [FromBody] UpdateCategoryDto model)
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

        category.Title = model.Title;
        category.ParentId = model.ParentId;

        _context.Categories.Update(category);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Category updated successfully", category.Id });
    }

    // DELETE: api/categories/{id}
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(string id)
    {
        var category = await _context.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
        {
            return NotFound(new { Message = "Category not found" });
        }

        // 检查是否有子分类
        if (category.Children != null && category.Children.Any())
        {
            return BadRequest(new { Message = "Cannot delete category with children. Please delete children first." });
        }

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return Ok(new { Message = "Category deleted successfully" });
    }
}