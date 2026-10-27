using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.KnowledgeCategoriesViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Knowledge Category";
    }

    public required KnowledgeCategory Category { get; set; }

    /// <summary>
    /// Whether the directory has children.
    /// </summary>
    public bool HasChildren { get; set; }

    /// <summary>
    /// Whether the directory still contains knowledge points.
    /// </summary>
    public bool HasKnowledgePoints { get; set; }
}
