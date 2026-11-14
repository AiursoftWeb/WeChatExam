using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// Immutable purchase identity for one virtual order. Ordinary orders need no row.
/// Keep the exact signed JSON, not mutable catalog/configuration references;
/// AppKey, session_key and user signatures must never be stored here.
/// </summary>
public class VirtualPaymentSnapshot
{
    [Key]
    public Guid PaymentOrderId { get; init; }
    public PaymentOrder? PaymentOrder { get; init; }

    [MaxLength(64)]
    public required string AppId { get; init; }

    [MaxLength(128)]
    public required string OpenId { get; init; }

    // Optional, server-confirmed payment reference. Older writers and unpaid
    // purchases leave it null; never populate it from a phone or a refund claim.
    [MaxLength(128)]
    public string? PlatformOrderId { get; set; }

    // Observed by the server from query_order, never from a phone's OS or claims.
    // Historical rows stay unknown until explicitly verified.
    public VirtualPaymentPlatform? Platform { get; set; }

    // New synchronized purchases retain the exact immutable goods revision.
    // Older writers and manual-mapping purchases leave this null.
    public Guid? VirtualPaymentGoodsId { get; init; }
    public VirtualPaymentGoods? VirtualPaymentGoods { get; init; }

    [MaxLength(4096)]
    public required string SignData { get; init; }
}

public enum VirtualPaymentPlatform { WeChat = 0, Apple = 7 }
