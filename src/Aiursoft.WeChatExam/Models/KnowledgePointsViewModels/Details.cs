using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgePointsViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "KnowledgePoint Details";
    }

    public required KnowledgePoint KnowledgePoint { get; set; }
}
