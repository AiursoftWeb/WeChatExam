using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Services;

namespace Aiursoft.WeChatExam.Models.AccountingViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel(DateTime month)
    {
        PageTitle = $"Finance Report - {month:yyyy-MM}";
    }

    public required MonthlyActiveUserReport Report { get; set; }
}
