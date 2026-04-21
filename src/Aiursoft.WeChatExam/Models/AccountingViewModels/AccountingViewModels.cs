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
}

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Monthly Finance Report Details";
    }

    public required MonthlyActiveUserReport Report { get; set; }
}
