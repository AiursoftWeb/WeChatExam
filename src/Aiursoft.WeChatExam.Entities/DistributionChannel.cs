using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// 分销渠道实体，用于追踪机构/代理的推广效果
/// </summary>
public class DistributionChannel
{
    [Key]
    public Guid Id { get; init; }

    /// <summary>
    /// 系统生成的唯一分销码，用于用户注册时识别渠道
    /// 创建后不可修改
    /// </summary>
    [Required]
    [MaxLength(16)]
    public required string Code { get; init; }

    /// <summary>
    /// 机构/代理名称
    /// </summary>
    [Required]
    [MaxLength(200)]
    public required string AgencyName { get; set; }

    /// <summary>
    /// 渠道是否启用。禁用后不接受新用户绑定，但不影响历史统计
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 渠道创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 通过该渠道绑定的用户关联记录
    /// </summary>
    [InverseProperty(nameof(UserDistributionChannel.DistributionChannel))]
    public IEnumerable<UserDistributionChannel> UserDistributionChannels { get; init; } = new List<UserDistributionChannel>();
}
