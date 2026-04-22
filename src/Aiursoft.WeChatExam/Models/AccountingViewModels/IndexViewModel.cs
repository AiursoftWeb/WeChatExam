using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.AccountingViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Accounting Reports";
    }

    public List<MonthlyActiveUserReport> Reports { get; set; } = [];

    // General Stats
    public int TotalUsers { get; set; }
    public int ActivePaidUsers { get; set; }
    public int NewUsersLast30Days { get; set; }
    public int NewVipActivationsLast30Days { get; set; }

    // Chart Data
    public List<string> ChartLabels { get; set; } = [];
    public List<int> NewUsersData { get; set; } = [];
    public List<int> NewVipData { get; set; } = [];
}
