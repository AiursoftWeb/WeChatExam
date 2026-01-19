using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// 资讯信息小程序接口
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class ArticlesController(WeChatExamDbContext context) : ControllerBase
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
}
