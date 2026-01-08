using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TagsViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public required Tag Tag { get; set; }
    public int UsageCount { get; set; }
}
