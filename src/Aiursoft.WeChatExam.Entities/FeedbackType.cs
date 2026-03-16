using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

public enum FeedbackType
{
    [Display(Name = "功能建议")]
    Suggestion,
    [Display(Name = "内容错误")]
    ContentError,
    [Display(Name = "程序漏洞")]
    Bug,
    [Display(Name = "其他")]
    Other
}
