using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.DashboardViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
public class DashboardController(WeChatExamDbContext context) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Index",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;

        // === Summary counts ===
        var totalQuestions = await context.Questions.CountAsync();
        var totalPapers = await context.Papers.CountAsync();
        var totalExams = await context.Exams.CountAsync();
        var totalUsers = await context.Users.CountAsync();
        var totalCategories = await context.Categories.CountAsync();
        var totalKnowledgePoints = await context.KnowledgePoints.CountAsync();
        var totalTags = await context.Tags.CountAsync();
        var totalArticles = await context.Articles.CountAsync();

        // === Paper status breakdown ===
        var papersDraft = await context.Papers.CountAsync(p => p.Status == PaperStatus.Draft);
        var papersPublishable = await context.Papers.CountAsync(p => p.Status == PaperStatus.Publishable);
        var papersFrozen = await context.Papers.CountAsync(p => p.Status == PaperStatus.Frozen);

        // === Question type distribution ===
        var questionsChoice = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.Choice);
        var questionsBlank = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.Blank);
        var questionsBool = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.Bool);
        var questionsShortAnswer = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.ShortAnswer);
        var questionsEssay = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.Essay);
        var questionsNounExplanation = await context.Questions.CountAsync(q => q.QuestionType == QuestionType.NounExplanation);

        // === Exam statistics ===
        var examsActive = await context.Exams.CountAsync(e => e.StartTime <= now && e.EndTime > now);
        var examsUpcoming = await context.Exams.CountAsync(e => e.StartTime > now);
        var examsEnded = await context.Exams.CountAsync(e => e.EndTime <= now);

        var totalExamRecords = await context.ExamRecords.CountAsync();
        var examRecordsSubmitted = await context.ExamRecords.CountAsync(r => r.Status == ExamRecordStatus.Submitted);
        var examRecordsInProgress = await context.ExamRecords.CountAsync(r => r.Status == ExamRecordStatus.InProgress);
        var examRecordsTimedOut = await context.ExamRecords.CountAsync(r => r.Status == ExamRecordStatus.TimeOut);

        var averageExamScore = totalExamRecords > 0
            ? await context.ExamRecords
                .Where(r => r.Status == ExamRecordStatus.Submitted || r.Status == ExamRecordStatus.TimeOut)
                .AverageAsync(r => (double)r.TotalScore)
            : 0;

        // === Practice statistics ===
        var totalPracticeAttempts = await context.UserPracticeHistories.CountAsync();
        var practiceCorrect = await context.UserPracticeHistories.CountAsync(p => p.IsCorrect);
        var practiceAccuracy = totalPracticeAttempts > 0
            ? Math.Round((double)practiceCorrect / totalPracticeAttempts * 100, 1)
            : 0;

        // === Distribution channels ===
        var totalDistributionChannels = await context.DistributionChannels.CountAsync();
        var activeDistributionChannels = await context.DistributionChannels.CountAsync(d => d.IsEnabled);

        // === Top Tags (most used) ===
        // Note: QuestionTags links Questions and Tags. Group by TagId and count.
        // We need to join with Tags to get the name.
        var topTags = await context.Set<QuestionTag>()
            .GroupBy(qt => qt.TagId)
            .Select(g => new { TagId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .Join(context.Tags,
                  t => t.TagId,
                  tag => tag.Id,
                  (t, tag) => new TopTagItem { Name = tag.DisplayName, UsageCount = t.Count })
            .ToListAsync();

        // === Taxonomy Stats ===
        // Group tags by TaxonomyId (where TaxonomyId is not null)
        var taxonomyStats = await context.Tags
            .Where(t => t.TaxonomyId != null)
            .GroupBy(t => t.TaxonomyId)
            .Select(g => new { TaxonomyId = g.Key, Count = g.Count() })
            .Join(context.Set<Taxonomy>(),
                  g => g.TaxonomyId,
                  tax => tax.Id,
                  (g, tax) => new TaxonomyStatItem { Name = tax.Name, TagCount = g.Count })
            .OrderByDescending(x => x.TagCount)
            .ToListAsync();

        // === Recent exams (last 5) ===
        var recentExams = await context.Exams
            .OrderByDescending(e => e.CreationTime)
            .Take(5)
            .Select(e => new RecentExamItem
            {
                Id = e.Id,
                Title = e.Title,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                RecordCount = context.ExamRecords.Count(r => r.ExamId == e.Id),
                Status = e.EndTime <= now ? "Ended" :
                         e.StartTime <= now ? "Active" : "Upcoming",
                StatusBadgeClass = e.EndTime <= now ? "bg-secondary" :
                                   e.StartTime <= now ? "bg-success" : "bg-info"
            })
            .ToListAsync();

        // === Recent users (last 5) ===
        var recentUsers = await context.Users
            .OrderByDescending(u => u.CreationTime)
            .Take(5)
            .Select(u => new RecentUserItem
            {
                DisplayName = u.DisplayName,
                Email = u.Email,
                CreationTime = u.CreationTime
            })
            .ToListAsync();

        var model = new IndexViewModel
        {
            TotalQuestions = totalQuestions,
            TotalPapers = totalPapers,
            TotalExams = totalExams,
            TotalUsers = totalUsers,
            TotalCategories = totalCategories,
            TotalKnowledgePoints = totalKnowledgePoints,
            TotalTags = totalTags,
            TotalArticles = totalArticles,

            PapersDraft = papersDraft,
            PapersPublishable = papersPublishable,
            PapersFrozen = papersFrozen,

            QuestionsChoice = questionsChoice,
            QuestionsBlank = questionsBlank,
            QuestionsBool = questionsBool,
            QuestionsShortAnswer = questionsShortAnswer,
            QuestionsEssay = questionsEssay,
            QuestionsNounExplanation = questionsNounExplanation,

            ExamsActive = examsActive,
            ExamsUpcoming = examsUpcoming,
            ExamsEnded = examsEnded,
            TotalExamRecords = totalExamRecords,
            ExamRecordsSubmitted = examRecordsSubmitted,
            ExamRecordsInProgress = examRecordsInProgress,
            ExamRecordsTimedOut = examRecordsTimedOut,
            AverageExamScore = averageExamScore,

            TotalPracticeAttempts = totalPracticeAttempts,
            PracticeCorrect = practiceCorrect,
            PracticeAccuracyPercent = practiceAccuracy,

            TotalDistributionChannels = totalDistributionChannels,
            ActiveDistributionChannels = activeDistributionChannels,

            TopTags = topTags,
            TaxonomyStats = taxonomyStats,

            RecentExams = recentExams,
            RecentUsers = recentUsers
        };

        return this.StackView(model);
    }
}
