using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Exams";
    }

    public List<Exam> Exams { get; set; } = new();
}
