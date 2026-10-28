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
}
