using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PracticeViewModels;

public class PracticeViewModel : UiStackLayoutViewModel
{
    public PracticeViewModel()
    {
        PageTitle = "Practice Session";
    }

    public List<Question> Questions { get; set; } = new();
}
