using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class CategoryTaxonomy
{
    [Key]
    public Guid Id { get; init; }

    public required int TaxonomyId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(TaxonomyId))]
    [NotNull]
    public Taxonomy? Taxonomy { get; set; }

    public required Guid CategoryId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    [NotNull]
    public Category? Category { get; set; }
}
