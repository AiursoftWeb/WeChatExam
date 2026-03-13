using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 支付日志，记录每次状态变更或回调事件
/// </summary>
public class PaymentLog
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// 关联支付订单 ID
    /// </summary>
    public Guid PaymentOrderId { get; init; }

    [ForeignKey(nameof(PaymentOrderId))]
    public PaymentOrder? PaymentOrder { get; set; }

    /// <summary>
    /// 事件类型
    /// </summary>
    [Required]
    [MaxLength(50)]
    public required string EventType { get; init; }

    /// <summary>
    /// 原始数据（JSON 格式）
    /// </summary>
    public string? RawData { get; init; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
