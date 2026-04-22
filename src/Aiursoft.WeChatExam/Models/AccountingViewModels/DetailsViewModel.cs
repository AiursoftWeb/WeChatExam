using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.AccountingViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Monthly Finance Report Details";
    }

    public required MonthlyActiveUserReport Report { get; set; }
}
