using System.Text.Json;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class RefundManagementViewModel : UiStackLayoutViewModel
{
    public RefundManagementViewModel() => PageTitle = "Refund management";
    public IReadOnlyList<PaymentOrder> Orders { get; init; } = [];
    public int TotalCount { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public class RefundOrderViewModel : UiStackLayoutViewModel
{
    public RefundOrderViewModel() => PageTitle = "Refund handling";
    public required PaymentOrder Order { get; init; }
    public bool CanRequest => Order.Channel == PaymentChannel.WeChatVirtual &&
        Order.VirtualPaymentSnapshot?.Platform == VirtualPaymentPlatform.WeChat &&
        Order.Status == PaymentOrderStatus.Paid && Order.PaidAt >= DateTime.UtcNow.AddDays(-365) &&
        Order.AmountInFen > 0 && Order.RefundRequest == null && !NeedsReview(Order);

    public static bool NeedsReview(PaymentOrder order) => order.PaymentLogs
        .Where(l => l.EventType == "VirtualPaymentRefundReview")
        .Any(l => !RefundReviewItem.FromLog(l, order.PaymentLogs.Where(p => p.EventType == "VirtualPaymentRefunded").ToList()).IsResolved);

    public static bool RightsHandled(PaymentOrder order) => order.Status == PaymentOrderStatus.Refunded &&
        !NeedsReview(order) && order.PaymentLogs.Any(l => l.EventType == "VirtualPaymentRefunded");

    public static int? RefundAmount(PaymentOrder order)
    {
        if (order.RefundRequest is { } request) return request.AmountInFen;
        var amounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var log in order.PaymentLogs.Where(l => l.EventType is "VirtualPaymentRefunded" or "VirtualPaymentRefundReview"))
        {
            try
            {
                using var json = JsonDocument.Parse(log.RawData ?? "");
                var c = json.RootElement.GetProperty("confirmation");
                var id = c.GetProperty("PlatformRefundId").GetString();
                var amount = c.GetProperty("AmountInFen").GetInt32();
                if (c.GetProperty("PaymentOrderId").GetGuid() != order.Id || string.IsNullOrWhiteSpace(id) ||
                    amount <= 0 || amount > order.AmountInFen || amounts.TryGetValue(id, out var previous) && previous != amount) return null;
                amounts[id] = amount;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
        }
        var total = amounts.Values.Sum(x => (long)x);
        return total > 0 && total <= order.AmountInFen ? (int)total : null;
    }

    public static DateTime? RecordedAt(PaymentOrder order) => order.RefundRequest?.CreatedAt ?? order.PaymentLogs
        .Where(l => l.EventType is "VirtualPaymentRefunded" or "VirtualPaymentRefundReview")
        .Select(l => (DateTime?)l.CreatedAt).Min();
}
