using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.DTOs;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 资讯信息小程序接口
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class ArticlesController(TemplateDbContext context, IArticleImportService articleImportService) : ControllerBase
{
    /// <summary>
    /// 获取所有资讯信息
    /// </summary>
    /// <returns>资讯信息列表，按创建时间倒序</returns>
    /// <response code="200">成功返回资讯列表</response>
    [HttpGet("all")]
    [ProducesResponseType(typeof(List<ArticleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetArticlesAll()
    {
        var articles = await context.Articles
            .Include(a => a.Author)
            .OrderByDescending(a => a.CreationTime)
            .Select(a => new ArticleDto
            {
                Id = a.Id,
                Title = a.Title,
                Content = a.Content,
                CreationTime = a.CreationTime,
                AuthorName = a.Author != null ? a.Author.DisplayName : string.Empty
            })
            .ToListAsync();

        return Ok(articles);
    }

    /// <summary>
    /// 获取单个资讯详情
    /// </summary>
    /// <param name="id">资讯ID</param>
    /// <returns>资讯详情</returns>
    /// <response code="200">成功返回资讯详情</response>
    /// <response code="404">未找到指定资讯</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ArticleDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetArticle(Guid id)
    {
        var article = await context.Articles
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null)
        {
            return NotFound(new { Message = "Article not found" });
        }

        var articleDto = new ArticleDto
        {
            Id = article.Id,
            Title = article.Title,
            Content = article.Content,
            CreationTime = article.CreationTime,
            AuthorName = article.Author != null ? article.Author.DisplayName : string.Empty
        };

        return Ok(articleDto);
    }

    /// <summary>
    /// Import article and extract questions using AI
    /// </summary>
    /// <param name="importDto">Article import data</param>
    /// <returns>Import result</returns>
    /// <response code="200">Successfully imported article</response>
    /// <response code="400">Invalid import data</response>
    /// <response code="401">Unauthorized</response>
    [HttpPost("import")]
    [Authorize]
    [ProducesResponseType(typeof(ArticleImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ImportArticle([FromBody] ArticleImportDto importDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        // Get current user ID
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { Message = "User not authenticated" });
        }

        try
        {
            if (importDto.UseBackgroundJob)
            {
                // Process asynchronously
                var jobId = await articleImportService.ImportArticleAsync(importDto, userId);
                var result = new ArticleImportResultDto
                {
                    BackgroundJobId = jobId,
                    Status = ImportStatus.Pending
                };
                return Ok(result);
            }
            else
            {
                // Process synchronously
                var result = await articleImportService.ImportArticleAsync(importDto, userId);
                return Ok(result);
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = $"Import failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Extract questions from article content without importing
    /// </summary>
    /// <param name="request">Content extraction request</param>
    /// <returns>Extracted questions</returns>
    /// <response code="200">Successfully extracted questions</response>
    /// <response code="400">Invalid request</response>
    [HttpPost("extract-questions")]
    [Authorize]
    [ProducesResponseType(typeof(List<ExtractedQuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtractQuestions([FromBody] ContentExtractionRequestDto request)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(request.Content))
        {
            return BadRequest(ModelState);
        }

        try
        {
            var questions = await articleImportService.ExtractQuestionsFromContentAsync(request.Content);
            return Ok(questions);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = $"Extraction failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Extract tags from article content
    /// </summary>
    /// <param name="request">Content extraction request</param>
    /// <returns>Extracted tags</returns>
    /// <response code="200">Successfully extracted tags</response>
    /// <response code="400">Invalid request</response>
    [HttpPost("extract-tags")]
    [Authorize]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExtractTags([FromBody] ContentExtractionRequestDto request)
    {
        if (!ModelState.IsValid || string.IsNullOrEmpty(request.Content))
        {
            return BadRequest(ModelState);
        }

        try
        {
            var tags = await articleImportService.ExtractTagsFromContentAsync(request.Content);
            return Ok(tags);
        }
        catch (Exception ex)
        {
            return BadRequest(new { Message = $"Extraction failed: {ex.Message}" });
        }
    }
}
