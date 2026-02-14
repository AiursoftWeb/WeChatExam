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
public class ExamsController(
    WeChatExamDbContext context, 
    IExamService examService,
    IPaperService paperService) : Controller
{
    // GET: exams
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "clipboard-list",
        CascadedLinksOrder = 2,
        LinkText = "Exams",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var exams = await examService.GetAllExamsAsync();
        return this.StackView(new IndexViewModel { Exams = exams });
    }

    // GET: exams/create
    [Authorize(Policy = AppPermissionNames.CanAddExams)]
    public async Task<IActionResult> Create()
    {
        var papers = await paperService.GetPapersAvailableForExamAsync();
        return this.StackView(new CreateViewModel { AvailablePapers = papers });
    }

    // POST: exams/create
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanAddExams)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailablePapers = await paperService.GetPapersAvailableForExamAsync();
            return this.StackView(model);
        }

        try
        {
            // The input is from datetime-local, which is "Unspecified" kind but represents User's Local Time (wall clock).
            // We need to convert it to UTC for storage.
            // Assuming server local time matches user local time for simplicity in this context, 
            // or explicit conversion if we want to be strict.
            // Using .ToUniversalTime() on Unspecified treats it as Local.
            
            var startUtc = model.StartTime.ToUniversalTime();
            var endUtc = model.EndTime.ToUniversalTime();

            // Trim seconds/milliseconds (optional, but requested earlier) - do it on the UTC time
            startUtc = startUtc.AddSeconds(-startUtc.Second).AddMilliseconds(-startUtc.Millisecond);
            endUtc = endUtc.AddSeconds(-endUtc.Second).AddMilliseconds(-endUtc.Millisecond);
            
            await examService.CreateExamAsync(model.Title, model.PaperId!.Value, startUtc, endUtc, model.DurationMinutes);
            return RedirectToAction(nameof(Index));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("", ex.Message);
            model.AvailablePapers = await paperService.GetPapersAvailableForExamAsync();
            return this.StackView(model);
        }
    }

    // GET: exams/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditExams)]
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
            StartTime = exam.StartTime.ToLocalTime().AddSeconds(-exam.StartTime.ToLocalTime().Second).AddMilliseconds(-exam.StartTime.ToLocalTime().Millisecond), // Convert UTC to Local for datetime-local input
            EndTime = exam.EndTime.ToLocalTime().AddSeconds(-exam.EndTime.ToLocalTime().Second).AddMilliseconds(-exam.EndTime.ToLocalTime().Millisecond),
            DurationMinutes = exam.DurationMinutes,
            IsPublic = exam.IsPublic,
            AllowedAttempts = exam.AllowedAttempts,
            AllowReview = exam.AllowReview,
            ShowAnswerAfter = exam.ShowAnswerAfter
        });
    }

    // POST: exams/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditExams)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
         if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return this.StackView(model);

        try
        {
             // Convert to UTC
            var startUtc = model.StartTime.ToUniversalTime();
            var endUtc = model.EndTime.ToUniversalTime();

            // Trim seconds/milliseconds
            startUtc = startUtc.AddSeconds(-startUtc.Second).AddMilliseconds(-startUtc.Millisecond);
            endUtc = endUtc.AddSeconds(-endUtc.Second).AddMilliseconds(-endUtc.Millisecond);

            await examService.UpdateExamAsync(id, model.Title, startUtc, endUtc, 
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
    [Authorize(Policy = AppPermissionNames.CanReadExams)]
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
[Authorize(Policy = AppPermissionNames.CanReadExams)]
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
    [Authorize(Policy = AppPermissionNames.CanEditExams)]
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

    // GET: exams/delete/{id}
    [Authorize(Policy = AppPermissionNames.CanDeleteExams)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();
        var exam = await examService.GetExamAsync(id.Value);
        if (exam == null) return NotFound();

        // Calculate record count
        var count = await context.ExamRecords.CountAsync(r => r.ExamId == id.Value);

        return this.StackView(new DeleteViewModel
        {
            Id = exam.Id,
            Title = exam.Title,
            StartTime = exam.StartTime,
            EndTime = exam.EndTime,
            RecordCount = count
        });
    }

    // POST: exams/delete/{id}
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanDeleteExams)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(DeleteViewModel model)
    {
        await examService.DeleteExamAsync(model.Id);
        return RedirectToAction(nameof(Index));
    }
}
