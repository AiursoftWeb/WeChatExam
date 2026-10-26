using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// A course shared by question categories, knowledge point trees and mock papers.
/// It has no parent or free/paid flag: access is defined by its content and membership.
/// </summary>
public class TotalCategory
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    public int OrderIndex { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
