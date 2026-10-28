using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TestPapersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// Lets administrators test the current editable version of a paper without creating
/// official exam records or affecting student practice history.
/// </summary>
[Authorize(Policy = AppPermissionNames.CanReadExams)]
[LimitPerMin]
public class TestPapersController(
    WeChatExamDbContext context,
    IGradingService gradingService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "clipboard-list",
        CascadedLinksOrder = 2,
        LinkText = "Test Papers",
        LinkOrder = 3)]
    public async Task<IActionResult> Index(TestPaperKind kind = TestPaperKind.Mock, string? search = null,
        PaperStatus? status = null, int page = 1)
    {
        if (!ModelState.IsValid || !Enum.IsDefined(kind) || (status.HasValue && !Enum.IsDefined(status.Value)))
            return BadRequest();
        search = search?.Trim();
        var query = kind == TestPaperKind.Mock
            ? context.MockPapers.AsNoTracking().Select(p => new TestPaperListItemViewModel
            {
                Id = p.Id, Title = p.Title, TimeLimit = p.TimeLimit, Status = p.Status,
                GroupTitle = p.TotalCategory.Title, CreationTime = p.CreationTime,
                QuestionCount = context.MockPaperQuestions.Count(q => q.MockPaperId == p.Id),
                TotalScore = context.MockPaperQuestions.Where(q => q.MockPaperId == p.Id).Sum(q => (int?)q.Score) ?? 0
            })
            : context.RealPapers.AsNoTracking().Select(p => new TestPaperListItemViewModel
            {
                Id = p.Id, Title = p.Title, TimeLimit = p.TimeLimit, Status = p.Status,
                GroupTitle = p.Institution.Title, CreationTime = p.CreationTime,
                QuestionCount = context.RealPaperQuestions.Count(q => q.RealPaperId == p.Id),
                TotalScore = context.RealPaperQuestions.Where(q => q.RealPaperId == p.Id).Sum(q => (int?)q.Score) ?? 0
            });
        if (!string.IsNullOrEmpty(search)) query = query.Where(p => p.Title.Contains(search));
        if (status.HasValue) query = query.Where(p => p.Status == status);
        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        return this.StackView(new IndexViewModel
        {
            Kind = kind, Search = search, Status = status, Page = page, Total = total,
            Papers = await query.OrderByDescending(p => p.CreationTime).ThenBy(p => p.Id)
                .Skip((page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToListAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Take(Guid id, TestPaperKind? kind)
    {
        if (!ModelState.IsValid || kind == null || !Enum.IsDefined(kind.Value)) return BadRequest();
        var model = await BuildTakeViewModelAsync(id, kind.Value);
        if (model == null)
        {
            return NotFound();
        }

        if (model.Questions.Count == 0)
        {
            TempData["Error"] = "This paper has no questions to test.";
            return RedirectToAction(nameof(Index), new { kind = model.Kind });
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitViewModel model)
    {
        if (!ModelState.IsValid || model.Kind == null || !Enum.IsDefined(model.Kind.Value)) return BadRequest();
        var paper = await LoadPaperAsync(model.PaperId, model.Kind.Value);
        if (paper == null) return NotFound();
        var orderedQuestions = paper.Questions;
        if (orderedQuestions.Count == 0)
        {
            TempData["Error"] = "This paper has no questions to test.";
            return RedirectToAction(nameof(Index), new { kind = model.Kind });
        }

        var results = new List<TestPaperQuestionResultViewModel>(orderedQuestions.Count);
        var totalScore = 0;
        var answeredCount = 0;

        for (var index = 0; index < orderedQuestions.Count; index++)
        {
            var paperQuestion = orderedQuestions[index];
            var question = paperQuestion.Question;
            var userAnswer = model.Answers.GetValueOrDefault(question.Id) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(userAnswer))
            {
                answeredCount++;
            }

            // This is the same grading engine used by the mini-program and formal exams.
            // AiEval therefore performs a real AI grading request instead of using a preview stub.
            var grading = await gradingService.GradeAsync(
                userAnswer,
                question.StandardAnswer,
                question.GradingStrategy,
                paperQuestion.Score,
                question.Content);

            totalScore += grading.Score;
            results.Add(new TestPaperQuestionResultViewModel
            {
                Number = index + 1,
                Score = grading.Score,
                MaxScore = paperQuestion.Score,
                IsCorrect = grading.IsCorrect,
                Content = question.Content,
                UserAnswer = userAnswer,
                StandardAnswer = question.StandardAnswer,
                Explanation = question.Explanation,
                Comment = grading.Comment,
                QuestionType = question.QuestionType,
                GradingStrategy = question.GradingStrategy
            });
        }

        return View("Result", new ResultViewModel
        {
            PaperId = paper.Id,
            Kind = model.Kind,
            Title = paper.Title,
            Score = totalScore,
            TotalScore = orderedQuestions.Sum(q => q.Score),
            AnsweredCount = answeredCount,
            Questions = results
        });
    }

    private async Task<TakeViewModel?> BuildTakeViewModelAsync(Guid paperId, TestPaperKind kind)
    {
        var paper = await LoadPaperAsync(paperId, kind);
        if (paper == null) return null;
        return new TakeViewModel
        {
            PaperId = paper.Id, Kind = kind, Title = paper.Title, TimeLimit = paper.TimeLimit,
            TotalScore = paper.Questions.Sum(q => q.Score),
            Questions = paper.Questions.Select(pq => new TestPaperQuestionViewModel
            {
                Id = pq.Question.Id, Order = pq.Order, Score = pq.Score,
                Content = pq.Question.Content, QuestionType = pq.Question.QuestionType,
                GradingStrategy = pq.Question.GradingStrategy, Options = ParseOptions(pq.Question)
            }).ToList()
        };
    }

    // Teacher tests use the current template, including unpublished edits. Student exams continue
    // to use their locked snapshots; this read-only path never creates a snapshot or student history.
    private async Task<TestPaper?> LoadPaperAsync(Guid id, TestPaperKind kind)
    {
        var paper = kind == TestPaperKind.Mock
            ? await context.MockPapers.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new TestPaper { Id = p.Id, Title = p.Title, TimeLimit = p.TimeLimit }).SingleOrDefaultAsync()
            : await context.RealPapers.AsNoTracking().Where(p => p.Id == id)
                .Select(p => new TestPaper { Id = p.Id, Title = p.Title, TimeLimit = p.TimeLimit }).SingleOrDefaultAsync();
        if (paper == null) return null;
        var query = kind == TestPaperKind.Mock
            ? context.MockPaperQuestions.AsNoTracking().Where(q => q.MockPaperId == id)
                .Select(q => new TestQuestion { Question = q.Question, Order = q.Order, Score = q.Score })
            : context.RealPaperQuestions.AsNoTracking().Where(q => q.RealPaperId == id)
                .Select(q => new TestQuestion { Question = q.Question, Order = q.Order, Score = q.Score });
        paper.Questions = await query.OrderBy(q => q.Order).ThenBy(q => q.Question.Id).ToListAsync();
        return paper;
    }

    private sealed class TestPaper
    {
        public Guid Id { get; init; }
        public required string Title { get; init; }
        public int TimeLimit { get; init; }
        public List<TestQuestion> Questions { get; set; } = [];
    }

    private sealed class TestQuestion
    {
        public required Question Question { get; init; }
        public int Order { get; init; }
        public int Score { get; init; }
    }

    private static List<string> ParseOptions(Question question)
    {
        if (!string.IsNullOrWhiteSpace(question.Metadata))
        {
            try
            {
                var definition = new { options = new List<string>() };
                var metadata = JsonConvert.DeserializeAnonymousType(question.Metadata, definition);
                var options = metadata?.options
                    .Where(option => !string.IsNullOrWhiteSpace(option))
                    .ToList();
                if (options is { Count: > 0 })
                {
                    return options;
                }
            }
            catch (JsonException)
            {
                // Older questions can contain non-JSON metadata. They fall back to a text answer.
            }
        }

        if (question.QuestionType != QuestionType.Bool)
        {
            return [];
        }

        return question.StandardAnswer.Trim().ToLowerInvariant() switch
        {
            "true" or "false" => ["True", "False"],
            "对" or "错" => ["对", "错"],
            _ => ["正确", "错误"]
        };
    }
}
