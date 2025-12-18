using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

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