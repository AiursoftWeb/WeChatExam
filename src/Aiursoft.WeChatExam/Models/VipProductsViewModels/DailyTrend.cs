namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class DailySalesPoint
{
    public DateTime Date { get; init; }
    public long RevenueInFen { get; init; }
    public int PaidOrderCount { get; init; }
}

public class DailyReceiptsTrend
{
    public List<DailySalesPoint> Points { get; init; } = [];
    public int UndatedOrderCount { get; init; }
    public long RevenueInFen => Points.Sum(p => p.RevenueInFen);
    public int PaidOrderCount => Points.Sum(p => p.PaidOrderCount);
}
