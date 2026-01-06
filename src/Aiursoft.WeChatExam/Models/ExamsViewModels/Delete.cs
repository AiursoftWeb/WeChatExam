using Aiursoft.UiStack.Layout;


namespace Aiursoft.WeChatExam.Models.ExamsViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Exam";
    }

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int RecordCount { get; set; }
}
