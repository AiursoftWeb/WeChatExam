using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Entities;

// Immutable commercial payload per revision; only its delivery state changes.
[Index(nameof(AppId), nameof(Environment), nameof(OfferId), nameof(SourceProductId), nameof(Fingerprint), nameof(Attempt), IsUnique = true)]
[Index(nameof(AppId), nameof(Environment), nameof(OfferId), nameof(State), nameof(NextAttemptAt))]
public class VirtualPaymentGoods
{
    [Key] public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SourceProductId { get; init; }
    public Guid? VipProductId { get; set; }
    public VipProduct? VipProduct { get; set; }
    [MaxLength(64)] public required string AppId { get; init; }
    public int Environment { get; init; }
    [MaxLength(64)] public required string OfferId { get; init; }
    [MaxLength(64)] public required string Fingerprint { get; init; }
    [MaxLength(20)] public required string GoodsId { get; init; }
    [MaxLength(200)] public required string Name { get; init; }
    public int PriceInFen { get; init; }
    public int DurationDays { get; init; }
    [MaxLength(1024)] public required string Remark { get; init; }
    [MaxLength(2048)] public required string PictureUrl { get; init; }
    public VirtualGoodsState State { get; set; }
    public int Attempt { get; init; } = 1;
    public int Failures { get; set; }
    // Controlled, local error key only. Never persist gateway URLs or credentials.
    [MaxLength(64)] public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? PublishedAt { get; set; }
}

public enum VirtualGoodsState { Queued, Uploading, ReadyToPublish, Publishing, Published, Failed }

// One lease for both upload and publish task slots in this AppId/environment.
public class VirtualPaymentGoodsLease
{
    [Key, MaxLength(80)] public required string Id { get; init; }
    public Guid Owner { get; set; }
    public DateTime Until { get; set; } = DateTime.UnixEpoch;
}
