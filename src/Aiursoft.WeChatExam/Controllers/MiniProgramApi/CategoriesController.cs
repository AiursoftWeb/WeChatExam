using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
public class CategoriesController : ControllerBase
{
    private readonly WeChatExamDbContext _context;

    public CategoriesController(WeChatExamDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Retrieves all root categories with their children.
    /// </summary>
    /// <returns>A list of categories.</returns>
    /// <response code="200">Returns the list of categories.</response>
    [HttpGet("all")]
    [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
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

    /// <summary>
    /// Retrieves the immediate sub-categories of a specific category by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the parent category.</param>
    /// <returns>A list of immediate sub-categories.</returns>
    /// <response code="200">Returns the list of sub-categories.</response>
    /// <response code="404">If the parent category is not found.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCategory(Guid id)
    {
        var parentCategoryExists = await _context.Categories.AnyAsync(c => c.Id == id);
        if (!parentCategoryExists)
        {
            return NotFound(new { Message = "Parent category not found" });
        }

        var childrenCategories = await _context.Categories
            .Include(c => c.Children)
            .Where(c => c.ParentId == id)
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

        return Ok(childrenCategories);
    }

    /// <summary>
    /// Retrieves top-level categories (categories without a parent).
    /// </summary>
    /// <returns>A list of top-level categories.</returns>
    /// <response code="200">Returns the list of top-level categories.</response>
    [HttpGet("top")]
    [ProducesResponseType(typeof(List<CategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTop()
    {
        var categories = await _context.Categories
            .Where(c => c.ParentId == null)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Title = c.Title,
                Children = Array.Empty<Child>()
            })
            .ToListAsync();

        return Ok(categories);
    }
}