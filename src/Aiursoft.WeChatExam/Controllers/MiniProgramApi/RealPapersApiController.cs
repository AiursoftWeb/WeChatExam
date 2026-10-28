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
/// Reads published institution papers. School membership (or a valid legacy all-school
/// membership) covers the whole mixed-course paper, never independent question-bank rights.
/// </summary>
[Route("api/RealPapers")]
[ApiController]
[WeChatUserOnly]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RealPapersApiController(WeChatExamDbContext context, IPaperAccessService accessService) : ControllerBase
{
    private static bool CanRead(Guid institutionId, PaperAccessStatus access) =>
        access.HasRealExamVip || access.ActiveInstitutionVips.Contains(institutionId);

    [HttpGet("institutions")]
    [ProducesResponseType(typeof(List<RealPaperInstitutionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Institutions(CancellationToken cancellationToken)
    {
        // Navigation is independent of tags. Hide schools with no published paper so
        // draft-only imports do not create empty student menu entries.
        var schools = await context.Institutions.AsNoTracking()
            .Select(i => new
            {
                i.Id, i.Title, i.OrderIndex,
                PaperCount = context.RealPapers.Count(p => p.InstitutionId == i.Id &&
                    (p.Status == PaperStatus.Publishable || p.Status == PaperStatus.Frozen) &&
                    context.PaperSnapshots.Any(s => s.RealPaperId == p.Id))
            }).Where(i => i.PaperCount > 0).OrderBy(i => i.OrderIndex).ThenBy(i => i.Title).ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);
        var access = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(schools.Select(i => new RealPaperInstitutionDto(i.Id, i.Title, i.PaperCount,
            CanRead(i.Id, access))).ToList());
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<RealPaperSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Index(Guid? institutionId, CancellationToken cancellationToken)
    {
        if (institutionId.HasValue && !await context.Institutions.AnyAsync(c => c.Id == institutionId, cancellationToken))
            return NotFound(new { Message = "Institution not found." });
        var latest = context.PaperSnapshots.Where(s => s.RealPaperId != null)
            .GroupBy(s => s.RealPaperId).Select(g => new { RealPaperId = g.Key, Version = g.Max(s => s.Version) });
        // Read display metadata from the latest published version, never from an unfinished draft.
        var papers = await (from paper in context.RealPapers.AsNoTracking()
            where (paper.Status == PaperStatus.Publishable || paper.Status == PaperStatus.Frozen)
                && (!institutionId.HasValue || paper.InstitutionId == institutionId)
            join version in latest on paper.Id equals version.RealPaperId
            join snapshot in context.PaperSnapshots.AsNoTracking()
                on new { RealPaperId = (Guid?)paper.Id, version.Version } equals new { snapshot.RealPaperId, snapshot.Version }
            orderby snapshot.Title, paper.Id
            select new { Paper = paper, Snapshot = snapshot }).ToListAsync(cancellationToken);
        var access = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        return Ok(papers.Select(p => new RealPaperSummaryDto(p.Paper.Id, p.Paper.InstitutionId,
            p.Snapshot.Title, p.Snapshot.TimeLimit, CanRead(p.Paper.InstitutionId, access),
            p.Snapshot.Id, p.Snapshot.Version)).ToList());
    }

    [HttpGet("snapshots/{snapshotId:guid}")]
    [ProducesResponseType(typeof(RealPaperSnapshotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Snapshot(Guid snapshotId, CancellationToken cancellationToken)
    {
        var snapshot = await context.PaperSnapshots.AsNoTracking().Include(s => s.RealPaper)
            .SingleOrDefaultAsync(s => s.Id == snapshotId && s.RealPaperId != null, cancellationToken);
        var paper = snapshot?.RealPaper;
        if (paper == null || paper.Status is not (PaperStatus.Publishable or PaperStatus.Frozen))
            return NotFound(new { Message = "Published real paper not found." });
        var access = await accessService.GetUserAccessStatusAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
        // Every version requires school entitlement. Neither a snapshot's legacy IsFree flag
        // nor course membership can make a real paper free. The school is locked at publication.
        if (!CanRead(paper.InstitutionId, access))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active membership for this institution is required.",
                RequiredInstitutionId = paper.InstitutionId
            });
        var questions = await context.QuestionSnapshots.AsNoTracking().Where(q => q.PaperSnapshotId == snapshotId)
            .OrderBy(q => q.Order).ThenBy(q => q.OriginalQuestionId).ThenBy(q => q.Id)
            .Select(q => new QuestionSnapshotDto
            {
                Id = q.Id, Order = q.Order, Score = q.Score, Content = q.Content,
                QuestionType = q.QuestionType, Metadata = q.Metadata
            }).ToListAsync(cancellationToken);
        // Only examination content is returned. Answers and explanations belong to grading/review.
        return Ok(new RealPaperSnapshotDto(snapshot!.Id, paper.Id, paper.InstitutionId, snapshot.Version,
            snapshot.Title, snapshot.TimeLimit, questions));
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
        var snapshot = await context.PaperSnapshots.AsNoTracking().Include(s => s.RealPaper)
            .SingleOrDefaultAsync(s => s.Id == snapshotId && s.RealPaperId != null, cancellationToken);
        var paper = snapshot?.RealPaper;
        if (paper == null || paper.Status is not (PaperStatus.Publishable or PaperStatus.Frozen))
            return NotFound(new { Message = "Published real paper not found." });
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null)
            return Unauthorized();
        if (!await context.Users.AnyAsync(u => u.Id == userId, cancellationToken))
            return Unauthorized();
        var access = await accessService.GetUserAccessStatusAsync(userId);
        // Grading can incur a cost and reveals the answer. Apply the same current whole-paper
        // entitlement as reading, before either grading or writing a personal record.
        if (!CanRead(paper.InstitutionId, access))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                Message = "An active membership for this institution is required.",
                RequiredInstitutionId = paper.InstitutionId
            });
        var question = await context.QuestionSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(q => q.Id == dto.QuestionId && q.PaperSnapshotId == snapshotId, cancellationToken);
        if (question == null)
            return NotFound(new { Message = "Question snapshot not found in this paper version." });
        // Use the published payload, even after the original question changes or is deleted.
        // School entitlement includes every course in this paper without granting
        // independent question-bank practice rights.
        var grade = await gradingService.GradeAsync(dto.UserAnswer, question.StandardAnswer,
            question.GradingStrategy, question.Score, question.Content, question.Explanation);
        var result = new UserPracticeHistoryDto
        {
            QuestionId = question.Id, UserAnswer = dto.UserAnswer, StandardAnswer = question.StandardAnswer,
            Explanation = question.Explanation, IsCorrect = grade.IsCorrect, Score = grade.Score,
            PracticeType = dto.PracticeType, CreationTime = DateTime.UtcNow
        };
        // Retain the existing original-question history convention. A deleted source
        // still permits grading the published snapshot without creating an orphan history row.
        if (question.OriginalQuestionId.HasValue &&
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
