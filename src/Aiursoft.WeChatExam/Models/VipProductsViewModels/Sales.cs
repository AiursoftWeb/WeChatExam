using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class ProductSalesSummary
{
    public int PaidOrderCount { get; init; }
    public int BuyerCount { get; init; }
    public long RevenueInFen { get; init; }
    public int FreeOrderCount { get; init; }
    public int RefundedOrderCount { get; init; }
    public long RefundedAmountInFen { get; init; }
    public int RefundReviewCount { get; init; }
}

public class SalesPriceGroup
{
    public int AmountInFen { get; init; }
    public int OrderCount { get; init; }
    public int BuyerCount { get; init; }
    public long RevenueInFen { get; init; }
}

public class SalesViewModel : UiStackLayoutViewModel
{
    public SalesViewModel() => PageTitle = "Product sales";

    public required VipProduct Product { get; init; }
    public ProductSalesSummary Summary { get; init; } = new();
    public List<SalesPriceGroup> Prices { get; init; } = [];
    public List<PaymentOrder> Orders { get; init; } = [];
    public DailyReceiptsTrend? Trend { get; init; }
    public string Kind { get; init; } = "paid";
    public int TotalCount { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 25;
}
