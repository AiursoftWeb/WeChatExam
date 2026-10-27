using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.ContentSearchViewModels;

public class RecommendationRequest
{
    public Guid Id { get; set; }
    [MaxLength(20000)]
    public string? Text { get; set; }
    // Question periods are independent of the knowledge-point directory.
    public Guid? QuestionCategoryId { get; set; }
    [Required, MaxLength(5000)]
    public Guid[] ExcludeIds { get; set; } = [];
    public bool Refresh { get; set; }
}
