using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TotalCategoriesViewModels;

public class ContentsViewModel
{
    public TotalCategory Category { get; init; } = null!;
    public int PeriodCount { get; init; }
    public int QuestionCount { get; init; }
    public int KnowledgeCategoryCount { get; init; }
    public int KnowledgePointCount { get; init; }
    public int MockPaperCount { get; init; }
}
