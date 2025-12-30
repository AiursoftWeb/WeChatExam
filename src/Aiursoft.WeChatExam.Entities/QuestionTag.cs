using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class QuestionTag
{
    [Key]
    public int Id { get; init; }

    public required Guid QuestionId { get; set; }

    [ForeignKey(nameof(QuestionId))]
    [JsonIgnore]
    [NotNull]
    public Question? Question { get; set; }

    public required int TagId { get; set; }

    [ForeignKey(nameof(TagId))]
    [JsonIgnore]
    [NotNull]
    public Tag? Tag { get; set; }
}

