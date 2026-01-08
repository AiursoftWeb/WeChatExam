using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.BackgroundJobs;

public class JobsIndexViewModel : UiStackLayoutViewModel
{
    public IEnumerable<JobInfo> AllRecentJobs { get; init; } = [];
}
