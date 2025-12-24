using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete KnowledgePoint";
    }

    public required KnowledgePoint KnowledgePoint { get; set; }

    /// <summary>
    /// 指示分类是否有子分类
    /// </summary>
    public bool HasChildren { get; set; }
}
