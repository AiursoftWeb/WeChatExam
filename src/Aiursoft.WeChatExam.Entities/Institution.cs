using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// An independent school owning real exam papers and institution memberships.
/// It has no parent, course or pricing flag; it is not a question tag.
/// </summary>
public class Institution
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(200)]
    public required string Title { get; set; }

    public int OrderIndex { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
