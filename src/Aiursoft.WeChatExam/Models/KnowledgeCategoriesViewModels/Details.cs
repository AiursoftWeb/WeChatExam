using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Knowledge Category Details";
    }

    public required KnowledgeCategory Category { get; set; }

    public List<KnowledgePoint> AssociatedKnowledgePoints { get; set; } = new();
}
