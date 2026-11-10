using System.Text.Json;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class RefundReviewsViewModel : UiStackLayoutViewModel
{
    public RefundReviewsViewModel() => PageTitle = "Refund reconciliation records";
    public IReadOnlyList<RefundReviewItem> Reviews { get; init; } = [];
    public int TotalCount { get; init; }
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public Guid? OrderIdFilter { get; init; }
}

/// <summary>
/// Read-only projection of durable refund evidence. Malformed/old audit data
/// stays visible without treating missing amounts or identities as valid proof.
/// </summary>
public sealed class RefundReviewItem
{
    public required PaymentOrder Order { get; init; }
    public DateTime RecordedAt { get; init; }
    public string Reason { get; private set; } = "unreadable";
    public string? PlatformRefundId { get; private set; }
    public int? AmountInFen { get; private set; }

    public static RefundReviewItem FromLog(PaymentLog log)
    {
        var item = new RefundReviewItem { Order = log.PaymentOrder!, RecordedAt = log.CreatedAt };
        try
        {
            using var json = JsonDocument.Parse(log.RawData ?? "");
            var confirmation = json.RootElement.GetProperty("confirmation");
            var refund = confirmation.GetProperty("PlatformRefundId").GetString();
            var amount = confirmation.GetProperty("AmountInFen").GetInt32();
            var reason = json.RootElement.GetProperty("reason").GetString();
            if (confirmation.GetProperty("PaymentOrderId").GetGuid() != log.PaymentOrderId ||
                string.IsNullOrWhiteSpace(refund) || refund.Length > 128 || amount <= 0 ||
                amount > item.Order.AmountInFen || string.IsNullOrWhiteSpace(reason)) return item;
            item.Reason = reason;
            item.PlatformRefundId = refund;
            item.AmountInFen = amount;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Do not fail the whole page or render raw JSON as a business explanation.
        }
        return item;
    }
}
