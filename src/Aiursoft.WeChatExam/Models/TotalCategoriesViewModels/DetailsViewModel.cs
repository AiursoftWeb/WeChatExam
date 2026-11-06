using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TotalCategoriesViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Course Contents";
    }

    public required TotalCategory Course { get; init; }
    public List<QuestionCategory> Periods { get; init; } = [];
    public int PeriodCount { get; init; }
    public List<KnowledgeCategory> RootKnowledgeCategories { get; init; } = [];
    public int KnowledgePointCount { get; init; }
    public int KnowledgeCategoryCount { get; init; }
    public List<MockPaper> MockPapers { get; init; } = [];
    public int MockPaperCount { get; init; }
    public bool CanReadQuestions { get; init; }
    public bool CanEditPapers { get; init; }
}
