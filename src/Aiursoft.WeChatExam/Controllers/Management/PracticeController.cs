using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PracticeViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class PracticeController(
    WeChatExamDbContext context,
    IGradingService gradingService,
    IStringLocalizer<PracticeController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "award",
        CascadedLinksOrder = 2,
        LinkText = "Practice Test",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? mtql, QuestionType? questionType)
    {
        var query = context.Questions.AsQueryable();

        if (questionType.HasValue)
        {
            query = query.Where(q => q.QuestionType == questionType.Value);
        }

        if (!string.IsNullOrWhiteSpace(mtql))
        {
            try
            {
                var tokens = MTQL.Services.Tokenizer.Tokenize(mtql);
                var rpn = MTQL.Services.Parser.ToRpn(tokens);
                var ast = MTQL.Services.AstBuilder.Build(rpn);
                var predicate = MTQL.Services.PredicateBuilder.Build(ast);
                query = query.Where(predicate);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(mtql), $"Invalid MTQL: {ex.Message}");
            }
        }

        var questions = await query
            .OrderByDescending(q => q.CreationTime)
            .Take(100)
            .ToListAsync();

        var questionTypeOptions = Enum.GetValues<QuestionType>()
            .Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = localizer[t.GetDisplayName()],
                Selected = questionType == t
            });

        return this.StackView(new IndexViewModel
        {
            Mtql = mtql,
            QuestionType = questionType,
            QuestionTypeOptions = questionTypeOptions,
            Questions = questions
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid[]? questionIds)
    {
        if (questionIds == null || questionIds.Length == 0)
        {
            TempData["Error"] = "Please select at least one question.";
            return RedirectToAction(nameof(Index));
        }

        var questions = await context.Questions
            .Where(q => questionIds.Contains(q.Id))
            .ToListAsync();

        // Sort them as per the input array if needed, or just random
        var sortedQuestions = questionIds
            .Select(id => questions.FirstOrDefault(q => q.Id == id))
            .Where(q => q != null)
            .Cast<Question>()
            .ToList();

        return this.StackView(new PracticeViewModel
        {
            Questions = sortedQuestions
        }, "Practice");
    }

    [HttpPost]
    public async Task<IActionResult> Grade(Guid questionId, string? userAnswer)
    {
        var question = await context.Questions.FindAsync(questionId);
        if (question == null) return NotFound();

        var result = await gradingService.GradeAsync(question, userAnswer ?? string.Empty);
        return Json(new
        {
            isCorrect = result.IsCorrect,
            score = result.Score,
            comment = result.Comment,
            standardAnswer = question.StandardAnswer,
            explanation = question.Explanation
        });
    }
}
