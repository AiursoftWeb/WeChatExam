using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.WeChatExam.Entities;

public class Tag
{
    [Key]
    public int Id { get; init; }

    [Required]
    [MaxLength(255)]
    public required string DisplayName { get; set; }

    [Required]
    [MaxLength(255)]
    public required string NormalizedName { get; set; }

    public int? TaxonomyId { get; set; }

    [ForeignKey(nameof(TaxonomyId))]
    public Taxonomy? Taxonomy { get; set; }

    [InverseProperty(nameof(QuestionTag.Tag))]
    public IEnumerable<QuestionTag> QuestionTags { get; init; } = new List<QuestionTag>();
}
