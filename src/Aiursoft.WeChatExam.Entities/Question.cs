using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class Question
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 题干内容。可能包含文本、图片链接，或者填空题的下划线占位符（如 `____`）。
    /// </summary>
    [Required]
    [MaxLength(2000)]
    public required string Content { get; set; }

    /// <summary>
    /// 题目展示类型。决定前端渲染什么控件。
    /// Choice, Blank, Bool, ShortAnswer, Essay
    /// </summary>
    [Required]
    public QuestionType QuestionType { get; set; }

    /// <summary>
    /// 判卷方法。决定后端如何计算得分。
    /// ExactMatch, FuzzyMatch, AiEval
    /// </summary>
    [Required]
    public GradingStrategy GradingStrategy { get; set; }

    /// <summary>
    /// 题的Metadata（可能是选择题的Choices，是个JSON，后端不管，无脑返回前端）
    /// </summary>
    [MaxLength(5000)]
    public string Metadata { get; set; } = string.Empty;

    /// <summary>
    /// 判卷标准/正确答案。
    /// </summary>
    [MaxLength(5000)]
    public string StandardAnswer { get; set; } = string.Empty;

    /// <summary>
    /// 题目解析
    /// </summary>
    [MaxLength(3000)]
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// 所属分类的 ID（可为空，表示未分类）
    /// </summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; init; } = DateTime.UtcNow;

    // 导航引用：Category?, JsonIgnore, ForeignKey
    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    public Category? Category { get; set; }

    [InverseProperty(nameof(KnowledgePointQuestion.Question))]
    public IEnumerable<KnowledgePointQuestion> KnowledgePointQuestions { get; init; } = new List<KnowledgePointQuestion>();

    [InverseProperty(nameof(QuestionTag.Question))]
    public IEnumerable<QuestionTag> QuestionTags { get; init; } = new List<QuestionTag>();
}
