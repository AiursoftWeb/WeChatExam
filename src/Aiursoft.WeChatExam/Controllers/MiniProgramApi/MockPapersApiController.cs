using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>
/// Reads published mock papers. Course membership unlocks the whole paper, including
/// questions from paid periods; question-bank practice rights are checked separately.
/// </summary>
[Route("api/MockPapers")]
[ApiController]
[WeChatUserOnly]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MockPapersApiController(WeChatExamDbContext context, IPaperAccessService accessService) : ControllerBase
{
    private static bool CanRead(MockPaper paper, PaperAccessStatus access) =>
        paper.AllowViewAnonymousely || access.ActiveCategoryVips.Contains(paper.TotalCategoryId);

    [HttpGet]
    [ProducesResponseType(typeof(List<MockPaperSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Index(Guid? totalCategoryId, CancellationToken cancellationToken)
    {
        if (totalCategoryId.HasValue && !await context.TotalCategories.AnyAsync(c => c.Id == totalCategoryId, cancellationToken))
            return NotFound(new { Message = "Total category not found." });
        var latest = context.PaperSnapshots.Where(s => s.MockPaperId != null)
            .GroupBy(s => s.MockPaperId).Select(g => new { MockPaperId = g.Key, Version = g.Max(s => s.Version) });
        // Read display metadata from the latest published version, never from an unfinished draft.
        var papers = await (from paper in context.MockPapers.AsNoTracking()
            where (paper.Status == PaperStatus.Publishable || paper.Status == PaperStatus.Frozen)
                && (!totalCategoryId.HasValue || paper.TotalCategoryId == totalCategoryId)
            join version in latest on paper.Id equals version.MockPaperId
            join snapshot in context.PaperSnapshots.AsNoTracking()
                on new { MockPaperId = (Guid?)paper.Id, version.Version } equals new { snapshot.MockPaperId, snapshot.Version }
            orderby paper.AllowViewAnonymousely descending, snapshot.Title, paper.Id
            select new { Paper = paper, Snapshot = snapshot }).ToListAsync(cancellationToken);
        var access = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(papers.Select(p => new MockPaperSummaryDto(p.Paper.Id, p.Paper.TotalCategoryId,
            p.Snapshot.Title, p.Snapshot.TimeLimit, p.Paper.AllowViewAnonymousely, CanRead(p.Paper, access),
            p.Snapshot.Id, p.Snapshot.Version)).ToList());
    }

    [HttpGet("snapshots/{snapshotId:guid}")]
    [ProducesResponseType(typeof(MockPaperSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Snapshot(Guid snapshotId, CancellationToken cancellationToken)
    {
        var snapshot = await context.PaperSnapshots.AsNoTracking().Include(s => s.MockPaper)
            .SingleOrDefaultAsync(s => s.Id == snapshotId && s.MockPaperId != null, cancellationToken);
        var paper = snapshot?.MockPaper;
        if (paper == null || paper.Status is not (PaperStatus.Publishable or PaperStatus.Frozen))
            return NotFound(new { Message = "Published mock paper not found." });
        var access = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        // The current opening setting governs every version: an old free snapshot must not
        // bypass a paper that now requires course membership. Real-exam VIP is unrelated.
        if (!CanRead(paper, access))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active membership for this course is required.",
                RequiredTotalCategoryId = paper.TotalCategoryId
            });
        var questions = await context.QuestionSnapshots.AsNoTracking().Where(q => q.PaperSnapshotId == snapshotId)
            .OrderBy(q => q.Order).ThenBy(q => q.OriginalQuestionId).ThenBy(q => q.Id)
            .Select(q => new QuestionSnapshotDto
            {
                Id = q.Id, Order = q.Order, Score = q.Score, Content = q.Content,
                QuestionType = q.QuestionType, Metadata = q.Metadata
            }).ToListAsync(cancellationToken);
        // Only examination content is returned. Answers and explanations belong to grading/review.
        return Ok(new MockPaperSnapshotDto(snapshot!.Id, paper.Id, paper.TotalCategoryId, snapshot.Version,
            snapshot.Title, snapshot.TimeLimit, paper.AllowViewAnonymousely, questions));
    }

    [HttpPost("snapshots/{snapshotId:guid}/answers")]
    [ProducesResponseType(typeof(UserPracticeHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SubmitAnswer(Guid snapshotId, [FromBody] CreateUserPracticeHistoryDto dto,
        [FromServices] IGradingService gradingService, CancellationToken cancellationToken)
    {
        if (dto.PracticeType.HasValue && !Enum.IsDefined(dto.PracticeType.Value))
            return BadRequest(new { Message = "Invalid answer submission." });
        var snapshot = await context.PaperSnapshots.AsNoTracking().Include(s => s.MockPaper)
            .SingleOrDefaultAsync(s => s.Id == snapshotId && s.MockPaperId != null, cancellationToken);
        var paper = snapshot?.MockPaper;
        if (paper == null || paper.Status is not (PaperStatus.Publishable or PaperStatus.Frozen))
            return NotFound(new { Message = "Published mock paper not found." });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != null && !await context.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return Unauthorized();
        var access = await accessService.GetUserAccessStatusAsync(userId);
        // Grading can incur a cost and reveals the answer. Apply the same current whole-paper
        // entitlement as reading, before either grading or writing a personal record.
        if (!CanRead(paper, access))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active membership for this course is required.",
                RequiredTotalCategoryId = paper.TotalCategoryId
            });
        var question = await context.QuestionSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(q => q.Id == dto.QuestionId && q.PaperSnapshotId == snapshotId, cancellationToken);
        if (question == null)
            return NotFound(new { Message = "Question snapshot not found in this paper version." });
        // Use the published payload, even after the original question changes or is deleted.
        // An open mock paper deliberately includes paid-period questions without granting
        // independent question-bank practice rights.
        var grade = await gradingService.GradeAsync(dto.UserAnswer, question.StandardAnswer,
            question.GradingStrategy, question.Score, question.Content, question.Explanation);
        var result = new UserPracticeHistoryDto
        {
            QuestionId = question.Id, UserAnswer = dto.UserAnswer, StandardAnswer = question.StandardAnswer,
            Explanation = question.Explanation, IsCorrect = grade.IsCorrect, Score = grade.Score,
            PracticeType = dto.PracticeType, CreationTime = DateTime.UtcNow
        };
        // Guests never create accounts or personal history. For members, retain the existing
        // original-question history convention; a deleted source still permits snapshot grading.
        if (userId != null && question.OriginalQuestionId.HasValue &&
            await context.Questions.AnyAsync(q => q.Id == question.OriginalQuestionId, cancellationToken))
        {
            var history = new UserPracticeHistory
            {
                Id = Guid.NewGuid(), UserId = userId, QuestionId = question.OriginalQuestionId.Value,
                UserAnswer = dto.UserAnswer, IsCorrect = grade.IsCorrect, PracticeType = dto.PracticeType,
                CreationTime = result.CreationTime
            };
            context.UserPracticeHistories.Add(history);
            await context.SaveChangesAsync(cancellationToken);
            result.Id = history.Id;
        }
        return Ok(result);
    }
}
