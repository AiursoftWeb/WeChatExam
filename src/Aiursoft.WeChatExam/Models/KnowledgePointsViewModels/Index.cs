using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "KnowledgePoints";
    }

    public List<KnowledgePoint> KnowledgePoints { get; set; } = new();

    public List<KnowledgePoint> RootKnowledgePoints { get; set; } = new();
}
