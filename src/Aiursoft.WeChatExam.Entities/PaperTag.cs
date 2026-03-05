using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class PaperTag
{
    [Key]
    public int Id { get; init; }

    public required Guid PaperId { get; set; }

    [ForeignKey(nameof(PaperId))]
    [JsonIgnore]
    [NotNull]
    public Paper? Paper { get; set; }

    public required int TagId { get; set; }

    [ForeignKey(nameof(TagId))]
    [JsonIgnore]
    [NotNull]
    public Tag? Tag { get; set; }
}
