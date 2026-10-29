using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

public class Taxonomy
{
    [Key]
    public int Id { get; init; }

    [Required]
    [MaxLength(50)]
    public required string Name { get; set; }

    public DateTime CreationTime { get; set; } = DateTime.UtcNow;

    [InverseProperty(nameof(Tag.Taxonomy))]
    public IEnumerable<Tag> Tags { get; init; } = new List<Tag>();
}
