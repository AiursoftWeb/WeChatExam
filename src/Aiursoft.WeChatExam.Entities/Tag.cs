using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Entities;

public class Tag
{
    [Key]
    public int Id { get; init; }

    [Required]
    [MaxLength(50)]
    public required string Name { get; set; }

    [MaxLength(50)]
    public string? Category { get; set; } // e.g. Year, School, Subject, AI_Generated

    [InverseProperty(nameof(QuestionTag.Tag))]
    public IEnumerable<QuestionTag> QuestionTags { get; init; } = new List<QuestionTag>();
}
