using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Exam Details";
    }

    public Exam? Exam { get; set; }
    public List<ExamRecord> Records { get; set; } = new();
}
