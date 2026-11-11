using System.Text.Json;
using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;

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
    public bool IsResolved { get; private set; }
    public bool CanReconcile => !IsResolved && Reason == "payment_not_locally_settled" &&
        AmountInFen == Order.AmountInFen && Order.Status is PaymentOrderStatus.Pending or PaymentOrderStatus.Failed;
    public string Reason { get; private set; } = "unreadable";
    public string? PlatformRefundId { get; private set; }
    public int? AmountInFen { get; private set; }

    public static RefundReviewItem FromLog(PaymentLog log, IReadOnlyList<PaymentLog>? resolutions = null)
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
            item.IsResolved = item.Order.Status == PaymentOrderStatus.Refunded && item.Order.PaidAt == null &&
                reason == "payment_not_locally_settled" && resolutions?.Count == 1 &&
                VirtualPaymentEarlyRefundService.TryReadReview(log, out var saved) && saved != null &&
                saved.PaymentOrderId == item.Order.Id && saved.AmountInFen == item.Order.AmountInFen &&
                VirtualPaymentEarlyRefundService.IsSameResolution(resolutions[0], log.Id, saved);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // Do not fail the whole page or render raw JSON as a business explanation.
        }
        return item;
    }
}
