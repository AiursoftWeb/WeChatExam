using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

public enum QuestionType
{
    [Display(Name = "选择题")]
    Choice,
    [Display(Name = "填空题")]
    Blank,
    [Display(Name = "判断题")]
    Bool,
    [Display(Name = "简答题")]
    ShortAnswer,
    [Display(Name = "论述题")]
    Essay,
    [Display(Name = "名词解释")]
    NounExplanation
}

