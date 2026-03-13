using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class IndexViewModel: UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Payment Orders";
    }

    public List<PaymentOrder> Orders { get; set; } = [];
    public PaymentOrderStatus? StatusFilter { get; set; }
    public string? UserIdFilter { get; set; }
    public int TotalCount { get; set; }
}
