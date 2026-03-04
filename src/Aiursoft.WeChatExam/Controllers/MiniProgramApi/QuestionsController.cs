using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
[WeChatUserOnly]
public class QuestionsController : ControllerBase
{
    private readonly WeChatExamDbContext _context;

    public QuestionsController(WeChatExamDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取题目列表
    /// </summary>
    /// <param name="categoryId">分类ID (可选)</param>
    /// <param name="tagName">Tag 显示名称 (可选)</param>
    /// <param name="mtql">MTQL 查询表达式 (可选，优先级高于 tagName)。例如: `rock &amp;&amp; not metal`</param>
    /// <param name="type">题目类型 (可选)</param>
    /// <param name="randomSize">随机获得题目的数量(可选，不可与 size 同时使用)</param>
    /// <param name="size">非随机(按时间排序)获得题目的数量，需要和 page 配合使用(可选)</param>
    /// <param name="page">分页的页码(可选，不能和 resumeType 混用)</param>
    /// <param name="resumeType">从哪里继续答题，刷题类型的枚举，暂时需要题型刷题0,不能和 page 混用（可选)</param>
    /// <returns>题目列表</returns>
    /// <response code="200">成功返回题目列表</response>
    /// <response code="404">指定的分类不存在</response>
    /// <response code="400">参数错误</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<QuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] Guid? categoryId,
        [FromQuery] string? tagName,
        [FromQuery] string? mtql,
        [FromQuery] QuestionType? type,
        [FromQuery] int? randomSize,
        [FromQuery] int? size,
        [FromQuery] int? page,
        [FromQuery] PracticeType? resumeType)
    {
        // 1. Parameter Validation
        // Gate: prevent fetching all questions when no filtering or limiting parameters are provided
        if (!categoryId.HasValue &&
            string.IsNullOrWhiteSpace(tagName) &&
            string.IsNullOrWhiteSpace(mtql) &&
            !type.HasValue &&
            !randomSize.HasValue &&
            !size.HasValue &&
            !page.HasValue &&
            !resumeType.HasValue)
        {
            return BadRequest(new { Message = "At least one query parameter must be provided." });
        }
        
        if (randomSize.HasValue && size.HasValue)
        {
             return BadRequest(new { Message = "RandomSize and size cannot be used together." });
        }
        
        if (page.HasValue && resumeType.HasValue)
        {
             return BadRequest(new { Message = "Page and resumeType cannot be used together." });
        }
        
        if (randomSize.HasValue && (page.HasValue || resumeType.HasValue))
        {
            return BadRequest(new { Message = "RandomSize cannot be used with page or resumeType." });
        }

        if (randomSize > 50)
        {
            return BadRequest(new { Message = "RandomSize cannot be greater than 50." });
        }

        if (size > 50)
        {
            return BadRequest(new { Message = "Size cannot be greater than 50." });
        }

        // 2. Base Query Construction (Filters)
        
        // Validate Category
        if (categoryId.HasValue)
        {
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.Id == categoryId);

            if (!categoryExists)
            {
                return NotFound(new { Message = "Category not found" });
            }
        }

        var query = _context.Questions.AsQueryable();

        if (categoryId.HasValue)
        {
            query = query.Where(q => q.CategoryId == categoryId.Value);
        }

        if (type.HasValue)
        {
            query = query.Where(q => q.QuestionType == type.Value);
        }

        // Apply Tag / MTQL Filters
        if (!string.IsNullOrWhiteSpace(mtql))
        {
            try
            {
                var tokens = MTQL.Services.Tokenizer.Tokenize(mtql);
                var rpn = MTQL.Services.Parser.ToRpn(tokens);
                var ast = MTQL.Services.AstBuilder.Build(rpn);
                var predicate = MTQL.Services.PredicateBuilder.Build(ast);
                query = query.Where(predicate);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = $"Invalid MTQL query: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Message = $"Error parsing MTQL query: {ex.Message}" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(tagName))
        {
            var normalizedTagName = tagName.Trim().ToUpperInvariant();
            query = query.Include(q => q.QuestionTags).ThenInclude(qt => qt.Tag)
                .Where(q => q.QuestionTags.Any(qt => qt.Tag.NormalizedName == normalizedTagName));
        }

        List<QuestionDto> questions = new List<QuestionDto>();

        // 3. Execution Modes

        // Mode 1: Random (RandomSize)
        if (randomSize.HasValue)
        {
             // To efficiently pick random rows, we fetch IDs first
             var ids = await query.Select(q => q.Id).ToListAsync();
             
             if (ids.Any())
             {
                 var count = Math.Min(randomSize.Value, ids.Count);
                 var selectedIds = ids.OrderBy(_ => Guid.NewGuid()).Take(count).ToList();

                 var randomQuestions = await _context.Questions
                     .Where(q => selectedIds.Contains(q.Id))
                     .Select(q => new QuestionDto
                     {
                         QuestionType = q.QuestionType,
                         Id = q.Id,
                         Content = q.Content,
                         Metadata = q.Metadata,
                         Order = 0,
                         Score = 10
                     })
                     .ToListAsync();
                
                 // Re-sort in random order as selected
                 questions = randomQuestions.OrderBy(q => selectedIds.IndexOf(q.Id)).ToList();
             }
        }
        // Mode 3: Resume (ResumeType + Size)
        else if (resumeType.HasValue) // Size is implied required or defaults? Prompt says "resumeType ... cannot be mixed with page".
        {
             // Ensure size is present, default to 10 if not? Prompt says "size=50&resumeType=0".
             int limit = size ?? 10; 
             
             var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
             if (string.IsNullOrEmpty(userId)) return Unauthorized();

             // Find last practiced question's creation time
             // Join UserPracticeHistory -> Question to get Question.CreationTime
             // We want the LATEST practice record (CreationTime of history)
             var lastPracticeQuestionTime = await _context.UserPracticeHistories
                 .Where(h => h.UserId == userId && h.PracticeType == resumeType.Value)
                 .OrderByDescending(h => h.CreationTime) // Latest practice first
                 .Select(h => h.Question.CreationTime)
                 .FirstOrDefaultAsync();

             // If found (not DateTime.MinValue), filter questions created AFTER that time
             // Note: FirstOrDefaultAsync returns default(DateTime) which is MinValue if not found.
             if (lastPracticeQuestionTime != DateTime.MinValue)
             {
                 query = query.Where(q => q.CreationTime > lastPracticeQuestionTime);
             }

             questions = await query
                 .OrderBy(q => q.CreationTime) // Sequential: Oldest to Newest
                 .Take(limit)
                 .Select(q => new QuestionDto
                 {
                     QuestionType = q.QuestionType,
                     Id = q.Id,
                     Content = q.Content,
                     Metadata = q.Metadata,
                     Order = 0,
                     Score = 10
                 })
                 .ToListAsync();
        }
        // Mode 2: Pagination (Page + Size)
        else if (size.HasValue)
        {
             int pageIndex = page ?? 1;
             if (pageIndex < 1) pageIndex = 1;

             questions = await query
                 .OrderBy(q => q.CreationTime) // Sequential: Oldest to Newest
                 .Skip((pageIndex - 1) * size.Value)
                 .Take(size.Value)
                 .Select(q => new QuestionDto
                 {
                     QuestionType = q.QuestionType,
                     Id = q.Id,
                     Content = q.Content,
                     Metadata = q.Metadata,
                     Order = 0,
                     Score = 10
                 })
                 .ToListAsync();
        }
        // Default / Fallback (No size, no randomSize)
        else
        {
             // Default to page 1, size 10 to avoid scraping all questions.
             questions = await query
                 .OrderByDescending(q => q.CreationTime)
                 .Take(10)
                 .Select(q => new QuestionDto
                 {
                     QuestionType = q.QuestionType,
                     Id = q.Id,
                     Content = q.Content,
                     Metadata = q.Metadata,
                     Order = 0,
                     Score = 10
                 })
                 .ToListAsync();
        }

        return Ok(questions);
    }

    /// <summary>
    /// 获取单个题目详情
    /// </summary>
    /// <param name="id">题目ID</param>
    /// <returns>单个题目详情</returns>
    /// <response code="200">成功返回题目详情</response>
    /// <response code="404">未找到指定题目</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(QuestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuestion(Guid id)
    {
        var question = await _context.Questions
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null)
        {
            return NotFound(new { Message = "Question not found" });
        }

        var dto = new QuestionDto
        {
            QuestionType = question.QuestionType,
            Id = question.Id,
            Content = question.Content,
            Metadata = question.Metadata,
            Order = 0,
            Score = 10
        };

        return Ok(dto);
    }
}