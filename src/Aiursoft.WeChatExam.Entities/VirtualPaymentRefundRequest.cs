using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// One full-refund intent per purchase. Commit BEFORE external I/O, then dispatch
/// at most once. An uncertain outcome is query-only, including after a restart.
/// </summary>
public class VirtualPaymentRefundRequest
{
    [Key] public Guid PaymentOrderId { get; init; }
    public PaymentOrder? PaymentOrder { get; set; }
    [MaxLength(32)] public required string RefundOrderId { get; init; }
    [MaxLength(450)] public required string ActorId { get; init; }
    public int AmountInFen { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public VirtualPaymentRefundRequestState State { get; set; }
    [MaxLength(128)] public string? PlatformRefundId { get; set; }
    public int? ErrorCode { get; set; }
}

public enum VirtualPaymentRefundRequestState { Unconfirmed, Accepted, Failed, Completed }
