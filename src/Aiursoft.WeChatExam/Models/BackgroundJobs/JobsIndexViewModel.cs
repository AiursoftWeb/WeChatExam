using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.BackgroundJobs;

public class JobsIndexViewModel : UiStackLayoutViewModel
{
    public JobsIndexViewModel()
    {
        PageTitle = "Background Jobs";
    }

    public IEnumerable<JobInfo> AllRecentJobs { get; init; } = [];
}
