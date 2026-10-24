using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.ContentSearchViewModels;

public class RecommendationRequest
{
    public Guid Id { get; set; }
    [MaxLength(20000)]
    public string? Text { get; set; }
    [Required, MaxLength(1000)]
    public Guid[] CategoryIds { get; set; } = [];
    [Required, MaxLength(5000)]
    public Guid[] ExcludeIds { get; set; } = [];
    public bool Refresh { get; set; }
}
