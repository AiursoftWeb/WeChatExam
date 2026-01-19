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
    /// Retrieves a specific category by its ID.
    /// </summary>
    /// <param name="id">The unique identifier of the category.</param>
    /// <returns>The requested category details.</returns>
    /// <response code="200">Returns the category.</response>
    /// <response code="404">If the category is not found.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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


}