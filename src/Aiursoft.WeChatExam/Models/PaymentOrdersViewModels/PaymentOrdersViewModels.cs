using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class IndexViewModel: UiStackLayoutViewModel
{
    public List<PaymentOrder> Orders { get; set; } = [];
    public PaymentOrderStatus? StatusFilter { get; set; }
    public string? UserIdFilter { get; set; }
    public int TotalCount { get; set; }
}

public class DetailsViewModel: UiStackLayoutViewModel
{
    public required PaymentOrder Order { get; set; }
}

public class UserPaymentsViewModel: UiStackLayoutViewModel
{
    public required string UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public List<PaymentOrder> Orders { get; set; } = [];
    public List<VipMembership> VipMemberships { get; set; } = [];
}
