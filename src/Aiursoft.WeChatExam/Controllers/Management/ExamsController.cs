using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExamsViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditQuestions)]
public class ExamsController(
    TemplateDbContext context, 
    IExamService examService,
    IPaperService paperService) : Controller
{
    // GET: exams
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Exams",
        LinkOrder = 4)]
    public async Task<IActionResult> Index()
    {
        var exams = await examService.GetAllExamsAsync();
        return this.StackView(new IndexViewModel { Exams = exams });
    }

    // GET: exams/create
    public async Task<IActionResult> Create()
    {
        var papers = await paperService.GetAllPapersAsync();
        // Only allow publishable or frozen papers
        papers = papers.Where(p => p.Status != PaperStatus.Draft).ToList();

        return this.StackView(new CreateViewModel { AvailablePapers = papers });
    }

    // POST: exams/create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailablePapers = (await paperService.GetAllPapersAsync())
                .Where(p => p.Status != PaperStatus.Draft).ToList();
            return this.StackView(model);
        }

        try
        {
            await examService.CreateExamAsync(model.Title, model.PaperId, model.StartTime, model.EndTime, model.DurationMinutes);
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
             model.AvailablePapers = (await paperService.GetAllPapersAsync())
                .Where(p => p.Status != PaperStatus.Draft).ToList();
            return this.StackView(model);
        }
    }

    // GET: exams/{id}/edit
    public async Task<IActionResult> Edit(Guid? id)
    {
         if (id == null) return NotFound();
        var exam = await examService.GetExamAsync(id.Value);
        if (exam == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = exam.Id,
            Title = exam.Title,
            PaperTitle = exam.Paper?.Title ?? "Unknown",
            StartTime = exam.StartTime,
            EndTime = exam.EndTime,
            DurationMinutes = exam.DurationMinutes,
            IsPublic = exam.IsPublic,
            AllowedAttempts = exam.AllowedAttempts,
            AllowReview = exam.AllowReview,
            ShowAnswerAfter = exam.ShowAnswerAfter
        });
    }

    // POST: exams/{id}/edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
         if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return this.StackView(model);

        try
        {
            await examService.UpdateExamAsync(id, model.Title, model.StartTime, model.EndTime, 
                model.DurationMinutes, model.IsPublic, model.AllowedAttempts, model.AllowReview, model.ShowAnswerAfter);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            ModelState.AddModelError("", ex.Message);
            return this.StackView(model);
        }
    }

    // GET: exams/{id}/details
    public async Task<IActionResult> Details(Guid? id)
    {
         if (id == null) return NotFound();
        var exam = await examService.GetExamAsync(id.Value);
        if (exam == null) return NotFound();

        var records = await context.ExamRecords
            .Include(r => r.User)
            .Where(r => r.ExamId == id)
            .OrderByDescending(r => r.StartTime)
            .ToListAsync();

        return this.StackView(new DetailsViewModel
        {
            Exam = exam,
            Records = records
        });
    }

    // GET: exams/review/{recordId}
    public async Task<IActionResult> Review(Guid? recordId)
    {
        if (recordId == null) return NotFound();
        var record = await examService.GetExamRecordAsync(recordId.Value);
        if (record == null) return NotFound();

        var questions = await context.QuestionSnapshots
            .Where(q => q.PaperSnapshotId == record.PaperSnapshotId)
            .OrderBy(q => q.Order)
            .ToListAsync();

        return this.StackView(new ReviewViewModel
        {
            Record = record,
            Questions = questions,
            NewTotalScore = record.TotalScore,
            TeacherComment = record.TeacherComment
        });
    }

    // POST: exams/review/{recordId}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScore(Guid recordId, ReviewViewModel model)
    {
        try
        {
            await examService.UpdateScoreAsync(recordId, model.NewTotalScore, model.TeacherComment);
            TempData["Success"] = "Score updated successfully.";
            return RedirectToAction(nameof(Review), new { recordId });
        }
        catch (Exception ex)
        {
             TempData["Error"] = ex.Message;
             return RedirectToAction(nameof(Review), new { recordId });
        }
    }

    // POST: exams/delete/{id}
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        await examService.DeleteExamAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
