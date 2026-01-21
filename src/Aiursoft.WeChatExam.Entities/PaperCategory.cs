using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class PaperCategory
{
    [Key]
    public Guid Id { get; init; }

    public required Guid PaperId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(PaperId))]
    [NotNull]
    public Paper? Paper { get; set; }

    public required Guid CategoryId { get; set; }
    [JsonIgnore]
    [ForeignKey(nameof(CategoryId))]
    [NotNull]
    public Category? Category { get; set; }
}
