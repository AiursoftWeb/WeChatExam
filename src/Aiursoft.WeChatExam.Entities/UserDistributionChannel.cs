using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 用户与分销渠道的关联表
/// 记录用户首次注册时绑定的渠道信息，用于统计
/// </summary>
public class UserDistributionChannel
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 关联的用户ID
    /// </summary>
    public required string UserId { get; set; }

    /// <summary>
    /// 用户导航属性
    /// </summary>
    [JsonIgnore]
    [ForeignKey(nameof(UserId))]
    [NotNull]
    public User? User { get; set; }

    /// <summary>
    /// 关联的分销渠道ID
    /// </summary>
    public required Guid DistributionChannelId { get; set; }

    /// <summary>
    /// 分销渠道导航属性
    /// </summary>
    [JsonIgnore]
    [ForeignKey(nameof(DistributionChannelId))]
    [NotNull]
    public DistributionChannel? DistributionChannel { get; set; }

    /// <summary>
    /// 用户绑定渠道的时间
    /// </summary>
    public DateTime BoundAt { get; init; } = DateTime.UtcNow;
}
