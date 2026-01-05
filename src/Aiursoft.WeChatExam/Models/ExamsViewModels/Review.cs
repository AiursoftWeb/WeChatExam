using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class ReviewViewModel : UiStackLayoutViewModel
{
    public ReviewViewModel()
    {
        PageTitle = "Review Exam Record";
    }

    public ExamRecord? Record { get; set; }
    public List<QuestionSnapshot> Questions { get; set; } = new();
    
    // For manual grading
    public int NewTotalScore { get; set; }
    public string TeacherComment { get; set; } = string.Empty;
}
