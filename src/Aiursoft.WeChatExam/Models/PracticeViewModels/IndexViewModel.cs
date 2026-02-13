using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PracticeViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Practice Test";
    }

    public string? Mtql { get; set; }
    public List<Question> Questions { get; set; } = new();
}
