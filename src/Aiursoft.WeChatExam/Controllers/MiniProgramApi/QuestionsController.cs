using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/[controller]")]
[ApiController]
public class QuestionsController : ControllerBase
{
    private readonly TemplateDbContext _context;

    public QuestionsController(TemplateDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 获取题目列表
    /// </summary>
    /// <param name="categoryId">分类ID (可选)</param>
    /// <param name="tagName">Tag 显示名称 (可选)</param>
    /// <param name="mtql">MTQL 查询表达式 (可选，优先级高于 tagName)。例如: `rock && not metal`</param>
    /// <returns>题目列表，按创建时间倒序</returns>
    /// <response code="200">成功返回题目列表</response>
    /// <response code="404">指定的分类不存在</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<QuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetQuestions([FromQuery] Guid? categoryId, [FromQuery] string? tagName, [FromQuery] string? mtql)
    {
        // 验证分类是否存在
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

        // 优先使用 MTQL
        if (!string.IsNullOrWhiteSpace(mtql))
        {
            try
            {
                var tokens = MTQL.Services.Tokenizer.Tokenize(mtql);
                var rpn = MTQL.Services.Parser.ToRpn(tokens);
                var ast = MTQL.Services.AstBuilder.Build(rpn);
                var predicate = MTQL.Services.PredicateBuilder.Build(ast);
                
                // MTQL 谓词中包含对 Tags 的反向引用，因此可能需要 Include 确保数据加载（视 EF 配置而定，通常 Where 不需要 Include，但为了安全起见或后续 Select 需要）
                // PredicateBuilder 生成的表达式是 Expression<Func<Question, bool>>，可以直接用于 Where
                query = query.Where(predicate);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = $"Invalid MTQL query: {ex.Message}" });
            }
            catch (Exception ex)
            {
                // Log generic error if needed
                return BadRequest(new { Message = $"Error parsing MTQL query: {ex.Message}" });
            }
        }
        else if (!string.IsNullOrWhiteSpace(tagName))
        {
            var normalizedTagName = tagName.Trim().ToUpperInvariant();
            query = query.Include(q => q.QuestionTags).ThenInclude(qt => qt.Tag)
                .Where(q => q.QuestionTags.Any(qt => qt.Tag.NormalizedName == normalizedTagName));
        }

        var questions = await query
            .OrderByDescending(q => q.CreationTime)
            .Select(q => new QuestionDto
            {
                QuestionType = q.QuestionType,
                Value = new Value
                {
                    Id = q.Id,
                    Content = q.Content,
                    Metadata = q.Metadata,
                    Explanation = q.Explanation
                }
            })
            .ToListAsync();

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
            Value = new Value
            {
                Id = question.Id,
                Content = question.Content,
                Metadata = question.Metadata,
                Explanation = question.Explanation
            }
        };

        return Ok(dto);
    }


}
