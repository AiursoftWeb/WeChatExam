using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.FeedbacksViewModels;

public class IndexViewModel: UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Feedback";
    }
    public List<Feedback> Feedbacks { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public FeedbackStatus? StatusFilter { get; set; }
}
