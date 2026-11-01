using System.Linq.Expressions;
using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

/// <summary>Reviews only the student's captured copies, independently of live content and current membership.</summary>
[Route("api/Mistakes")]
[ApiController]
[WeChatUserOnly]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MistakesController(WeChatExamDbContext context) : ControllerBase
{
    private static readonly Expression<Func<MistakeRecord, MistakeDetailsDto>> Details = m => new MistakeDetailsDto(
        m.Id, m.Content, m.QuestionType, m.Metadata, m.UserAnswer, m.StandardAnswer, m.Explanation,
        m.Score, m.MaxScore, m.GradingComment, m.CreationTime, m.Source, m.PracticeType, m.SourceQuestionId,
        m.SourceQuestionSnapshotId, m.SourcePaperId, m.SourcePaperSnapshotId, m.SourcePaperVersion, m.SourcePaperTitle);

    private IQueryable<MistakeRecord> Owned(string userId) =>
        context.MistakeRecords.AsNoTracking().Where(m => m.UserId == userId);

    private sealed record QuestionIdentity(Guid Id, Guid? QuestionId, Guid? SnapshotId);

    private static IQueryable<MistakeRecord> SameQuestion(IQueryable<MistakeRecord> owned, QuestionIdentity identity)
    {
        // IDs are provenance, not foreign keys. Never join the current question/paper to review a copy.
        if (identity.QuestionId.HasValue) return owned.Where(m => m.SourceQuestionId == identity.QuestionId);
        if (identity.SnapshotId.HasValue)
            return owned.Where(m => m.SourceQuestionId == null && m.SourceQuestionSnapshotId == identity.SnapshotId);
        return owned.Where(m => m.Id == identity.Id);
    }

    private static Task<QuestionIdentity?> FindIdentity(IQueryable<MistakeRecord> owned, Guid id,
        CancellationToken cancellationToken) => owned.Where(m => m.Id == id)
        .Select(m => new QuestionIdentity(m.Id, m.SourceQuestionId, m.SourceQuestionSnapshotId))
        .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Lists distinct questions using their most recent wrong copy. No answers are included.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(MistakePageDto<MistakeSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Index([FromQuery] MistakeQueryDto query, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var owned = Owned(userId);
        var groups = owned.GroupBy(m => new
        {
            // Do not merge missing IDs or collide an original ID with a snapshot ID.
            Kind = m.SourceQuestionId.HasValue ? 0 : m.SourceQuestionSnapshotId.HasValue ? 1 : 2,
            Id = m.SourceQuestionId ?? m.SourceQuestionSnapshotId ?? m.Id
        });
        var total = await groups.CountAsync(cancellationToken);
        var latest = groups.Select(g => new
            {
                Id = g.OrderByDescending(m => m.CreationTime).ThenByDescending(m => m.Id).Select(m => m.Id).First(),
                Count = g.Count()
            });
        var items = await (from summary in latest
                join copy in owned on summary.Id equals copy.Id
                orderby copy.CreationTime descending, copy.Id descending
                select new MistakeSummaryDto(copy.Id, copy.Content, copy.QuestionType,
                    copy.Source, copy.SourcePaperTitle, copy.CreationTime, summary.Count))
            .Skip(query.Offset).Take(query.Count).ToListAsync(cancellationToken);
        return Ok(new MistakePageDto<MistakeSummaryDto>(total, query.Offset, query.Count, items));
    }

    /// <summary>Reads one exact historical copy, without loading the current original or checking a new purchase.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(MistakeDetailsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var result = await Owned(userId).Where(m => m.Id == id).Select(Details).SingleOrDefaultAsync(cancellationToken);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>Lists this question's wrong submissions, retaining each version's own content and answer.</summary>
    [HttpGet("{id:guid}/attempts")]
    [ProducesResponseType(typeof(MistakePageDto<MistakeDetailsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Attempts(Guid id, [FromQuery] MistakeQueryDto query, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var owned = Owned(userId);
        var identity = await FindIdentity(owned, id, cancellationToken);
        if (identity == null) return NotFound();
        var attempts = SameQuestion(owned, identity);
        var total = await attempts.CountAsync(cancellationToken);
        var items = await attempts.OrderByDescending(m => m.CreationTime).ThenByDescending(m => m.Id)
            .Skip(query.Offset).Take(query.Count).Select(Details).ToListAsync(cancellationToken);
        return Ok(new MistakePageDto<MistakeDetailsDto>(total, query.Offset, query.Count, items));
    }

    /// <summary>Removes every captured wrong attempt for this question from this student's notebook only.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var owned = Owned(userId);
        var identity = await FindIdentity(owned, id, cancellationToken);
        if (identity == null) return NotFound();
        await SameQuestion(owned, identity).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Clears only the current student's notebook. Answer history and exam results are untouched.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await Owned(userId).ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}
