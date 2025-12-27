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
    /// 获取指定分类下的所有题目
    /// </summary>
    /// <param name="categoryId">分类ID</param>
    /// <returns>题目列表，按创建时间倒序</returns>
    /// <response code="200">成功返回题目列表</response>
    /// <response code="404">指定的分类不存在</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<QuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuestions([FromQuery] Guid categoryId)
    {
        // 验证分类是否存在
        var categoryExists = await _context.Categories
            .AnyAsync(c => c.Id == categoryId);

        if (!categoryExists)
        {
            return NotFound(new { Message = "Category not found" });
        }

        var questions = await _context.Questions
            .Where(q => q.CategoryId == categoryId)
            .OrderByDescending(q => q.CreationTime)
            .Select(q => new QuestionDto
            {
                Type = q.Type,
                Value = new Value
                {
                    Id = q.Id,
                    Text = q.Text,
                    List = ParseJsonArray(q.List),
                    SingleCorrect = q.SingleCorrect,
                    FillInCorrect = ParseJsonArray(q.FillInCorrect),
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
            Type = question.Type,
            Value = new Value
            {
                Id = question.Id,
                Text = question.Text,
                List = ParseJsonArray(question.List),
                SingleCorrect = question.SingleCorrect,
                FillInCorrect = ParseJsonArray(question.FillInCorrect),
                Explanation = question.Explanation
            }
        };

        return Ok(dto);
    }

    /// <summary>
    /// 将 JSON 数组字符串解析为字符串数组
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>字符串数组，如果输入为空则返回空数组</returns>
    private static string[] ParseJsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            // 移除空格和换行
            json = json.Trim();
            if (json.StartsWith('[') && json.EndsWith(']'))
            {
                // 简单的 JSON 数组解析
                json = json[1..^1]; // 移除 [ 和 ]
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Array.Empty<string>();
                }

                var items = json.Split(',');
                return items
                    .Select(item => item.Trim().Trim('"'))
                    .Where(item => !string.IsNullOrEmpty(item))
                    .ToArray();
            }
        }
        catch
        {
            // 解析失败，返回空数组
            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }
}
