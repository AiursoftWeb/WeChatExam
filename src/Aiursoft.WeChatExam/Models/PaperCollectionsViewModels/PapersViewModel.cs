using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.PaperCollectionsViewModels;

public class PapersViewModel : UiStackLayoutViewModel
{
    public PapersViewModel() { PageTitle = "Add Papers to Collection"; }
    public Guid Id { get; set; }
    public Guid Revision { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public bool HasMore { get; set; }
    public List<CollectionPaper> Papers { get; set; } = [];
}
