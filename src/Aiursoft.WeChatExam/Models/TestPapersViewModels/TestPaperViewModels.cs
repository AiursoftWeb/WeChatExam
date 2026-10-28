using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.TestPapersViewModels;

// IDs can overlap between the independent paper tables. Carry the kind through every test action.
public enum TestPaperKind { Mock, Real }

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Test Papers";
    }

    public TestPaperKind Kind { get; set; }
    public string? Search { get; set; }
    public PaperStatus? Status { get; set; }
    public int Page { get; set; }
    public int Total { get; set; }
    public const int PageSize = 24;
    public List<TestPaperListItemViewModel> Papers { get; set; } = [];
}

public class TestPaperListItemViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TimeLimit { get; set; }
    public PaperStatus Status { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public int QuestionCount { get; set; }
    public int TotalScore { get; set; }
    public DateTime CreationTime { get; set; }
}

public class TakeViewModel : UiStackLayoutViewModel
{
    public TakeViewModel()
    {
        PageTitle = "Paper Test";
    }

    public Guid PaperId { get; set; }
    public TestPaperKind? Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public int TimeLimit { get; set; }
    public int TotalScore { get; set; }
    public List<TestPaperQuestionViewModel> Questions { get; set; } = [];
}

public class TestPaperQuestionViewModel
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public int Score { get; set; }
    public string Content { get; set; } = string.Empty;
    public QuestionType QuestionType { get; set; }
    public GradingStrategy GradingStrategy { get; set; }
    public List<string> Options { get; set; } = [];
}

public class SubmitViewModel
{
    public Guid PaperId { get; set; }
    public TestPaperKind? Kind { get; set; }
    public Dictionary<Guid, string> Answers { get; set; } = [];
}

public class ResultViewModel : UiStackLayoutViewModel
{
    public ResultViewModel()
    {
        PageTitle = "Paper Test Result";
    }

    public Guid PaperId { get; set; }
    public TestPaperKind? Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Score { get; set; }
    public int TotalScore { get; set; }
    public int AnsweredCount { get; set; }
    public List<TestPaperQuestionResultViewModel> Questions { get; set; } = [];
}

public class TestPaperQuestionResultViewModel
{
    public int Number { get; set; }
    public int Score { get; set; }
    public int MaxScore { get; set; }
    public bool IsCorrect { get; set; }
    public string Content { get; set; } = string.Empty;
    public string UserAnswer { get; set; } = string.Empty;
    public string StandardAnswer { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public QuestionType QuestionType { get; set; }
    public GradingStrategy GradingStrategy { get; set; }
}
