using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.DashboardViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Dashboard";
    }

    // === Summary counts ===
    public int TotalQuestions { get; set; }
    public int TotalPapers { get; set; }
    public int TotalExams { get; set; }
    public int TotalUsers { get; set; }
    public int TotalCategories { get; set; }
    public int TotalKnowledgePoints { get; set; }
    public int TotalTags { get; set; }
    public int TotalArticles { get; set; }

    // === Paper status breakdown ===
    public int PapersDraft { get; set; }
    public int PapersPublishable { get; set; }
    public int PapersFrozen { get; set; }

    // === Question type distribution ===
    public int QuestionsChoice { get; set; }
    public int QuestionsBlank { get; set; }
    public int QuestionsBool { get; set; }
    public int QuestionsShortAnswer { get; set; }
    public int QuestionsEssay { get; set; }
    public int QuestionsNounExplanation { get; set; }

    // === Exam statistics ===
    public int ExamsActive { get; set; }
    public int ExamsUpcoming { get; set; }
    public int ExamsEnded { get; set; }
    public int TotalExamRecords { get; set; }
    public int ExamRecordsSubmitted { get; set; }
    public int ExamRecordsInProgress { get; set; }
    public int ExamRecordsTimedOut { get; set; }
    public double AverageExamScore { get; set; }

    // === Practice statistics ===
    public int TotalPracticeAttempts { get; set; }
    public int PracticeCorrect { get; set; }
    public double PracticeAccuracyPercent { get; set; }

    // === Distribution channels ===
    public int TotalDistributionChannels { get; set; }
    public int ActiveDistributionChannels { get; set; }

    // === Tag & Taxonomy stats ===
    public List<TopTagItem> TopTags { get; set; } = [];
    public List<TaxonomyStatItem> TaxonomyStats { get; set; } = [];

    // === Recent data ===
    public List<RecentExamItem> RecentExams { get; set; } = [];
    public List<RecentUserItem> RecentUsers { get; set; } = [];
}

public class RecentExamItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int RecordCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusBadgeClass { get; set; } = string.Empty;
}

public class RecentUserItem
{
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime CreationTime { get; set; }
}

public class TopTagItem
{
    public string Name { get; set; } = string.Empty;
    public int UsageCount { get; set; }
}

public class TaxonomyStatItem
{
    public string Name { get; set; } = string.Empty;
    public int TagCount { get; set; }
}
