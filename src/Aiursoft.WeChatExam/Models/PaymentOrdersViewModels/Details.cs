using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class DetailsViewModel: UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Payment Order Details";
    }

    public required PaymentOrder Order { get; set; }
}
