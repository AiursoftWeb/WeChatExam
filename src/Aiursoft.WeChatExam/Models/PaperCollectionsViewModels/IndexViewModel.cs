using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaperCollectionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() { PageTitle = "Paper Collections"; }
    public bool Connected { get; set; }
    public List<CollectionCard> Collections { get; set; } = [];
}

public record CollectionCard(PaperCollection Collection, int Total, int Available, int Drafts);
public record CollectionPaper(Guid Id, string Title, PaperStatus Status, bool IsFree, bool IsRealExam, bool HasSnapshot);
