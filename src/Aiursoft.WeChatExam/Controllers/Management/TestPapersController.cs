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
    public async Task<IActionResult> Index()
    {
        var papers = await context.Papers
            .AsNoTracking()
            .OrderByDescending(p => p.CreationTime)
            .Select(p => new TestPaperListItemViewModel
            {
                Id = p.Id,
                Title = p.Title,
                TimeLimit = p.TimeLimit,
                Status = p.Status,
                IsRealExam = p.IsRealExam,
                QuestionCount = p.PaperQuestions.Count(),
                TotalScore = p.PaperQuestions.Sum(q => (int?)q.Score) ?? 0,
                CreationTime = p.CreationTime
            })
            .ToListAsync();

        return this.StackView(new IndexViewModel { Papers = papers });
    }

    [HttpGet]
    public async Task<IActionResult> Take(Guid id)
    {
        var model = await BuildTakeViewModelAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        if (model.Questions.Count == 0)
        {
            TempData["Error"] = "This paper has no questions to test.";
            return RedirectToAction(nameof(Index));
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(SubmitViewModel model)
    {
        var paper = await context.Papers
            .AsNoTracking()
            .Include(p => p.PaperQuestions)
            .ThenInclude(pq => pq.Question)
            .FirstOrDefaultAsync(p => p.Id == model.PaperId);

        if (paper == null)
        {
            return NotFound();
        }

        var orderedQuestions = paper.PaperQuestions
            .OrderBy(pq => pq.Order)
            .ThenBy(pq => pq.Id)
            .ToList();
        if (orderedQuestions.Count == 0)
        {
            TempData["Error"] = "This paper has no questions to test.";
            return RedirectToAction(nameof(Index));
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
            Title = paper.Title,
            Score = totalScore,
            TotalScore = orderedQuestions.Sum(q => q.Score),
            AnsweredCount = answeredCount,
            Questions = results
        });
    }

    private async Task<TakeViewModel?> BuildTakeViewModelAsync(Guid paperId)
    {
        var paper = await context.Papers
            .AsNoTracking()
            .Include(p => p.PaperQuestions)
            .ThenInclude(pq => pq.Question)
            .FirstOrDefaultAsync(p => p.Id == paperId);

        if (paper == null)
        {
            return null;
        }

        var questions = paper.PaperQuestions
            .OrderBy(pq => pq.Order)
            .ThenBy(pq => pq.Id)
            .Select(pq => new TestPaperQuestionViewModel
            {
                Id = pq.QuestionId,
                Order = pq.Order,
                Score = pq.Score,
                Content = pq.Question.Content,
                QuestionType = pq.Question.QuestionType,
                GradingStrategy = pq.Question.GradingStrategy,
                Options = ParseOptions(pq.Question)
            })
            .ToList();

        return new TakeViewModel
        {
            PaperId = paper.Id,
            Title = paper.Title,
            TimeLimit = paper.TimeLimit,
            TotalScore = questions.Sum(q => q.Score),
            Questions = questions
        };
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
