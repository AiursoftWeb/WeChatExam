using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// The single level of question classification within a course, displayed as a period.
/// Unlike internal tags, this category owns the free/paid practice setting.
/// </summary>
public class QuestionCategory
{
    [Key]
    public Guid Id { get; init; } = Guid.NewGuid();

    [MaxLength(255)]
    public required string Title { get; set; }

    public required Guid TotalCategoryId { get; set; }

    [ForeignKey(nameof(TotalCategoryId))]
    public TotalCategory? TotalCategory { get; set; }

    public bool IsFree { get; set; }

    public int OrderIndex { get; set; }

    public DateTime CreationTime { get; init; } = DateTime.UtcNow;
}
