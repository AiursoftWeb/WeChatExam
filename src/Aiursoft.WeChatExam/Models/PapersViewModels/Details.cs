using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PapersViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Paper Details";
    }

    public Paper? Paper { get; set; }

    public List<PaperQuestion> PaperQuestions { get; set; } = new();

    public List<PaperSnapshot> Snapshots { get; set; } = new();
}
