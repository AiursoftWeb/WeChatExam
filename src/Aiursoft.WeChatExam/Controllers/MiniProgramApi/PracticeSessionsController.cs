using System.Security.Claims;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Services.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.MiniProgramApi;

[Route("api/PracticeSessions")]
[ApiController]
[WeChatUserOnly]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PracticeSessionsController(WeChatExamDbContext db, RelatedQuestionService related,
    IPaperAccessService accessService, IGradingService grading) : ControllerBase
{
    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private IQueryable<PracticeSession> Owned => db.PracticeSessions.AsNoTracking()
        .Where(s => s.UserId == UserId).Include(s => s.Items);

    /// <summary>The client retains this UUID when retrying creation; candidates are never client-supplied.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Start(Guid id, StartPracticeSessionDto request, CancellationToken cancellationToken)
    {
        if (UserId == null) return Unauthorized();
        if (id == Guid.Empty || request.MistakeId == Guid.Empty) return BadRequest();
        var existing = await Owned.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (existing != null) return existing.SourceMistakeId == request.MistakeId
            ? Ok(await Project(existing, cancellationToken)) : Conflict();
        var mistake = await db.MistakeRecords.AsNoTracking()
            .SingleOrDefaultAsync(m => m.Id == request.MistakeId && m.UserId == UserId, cancellationToken);
        if (mistake == null) return NotFound();
        var access = await accessService.GetUserAccessStatusAsync(UserId);
        var candidates = await related.FindAsync(mistake, access, cancellationToken);
        if (!candidates.Available) return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var ids = candidates.Items.Select(q => q.Id).ToArray();
        var questions = await db.Questions.AsNoTracking().Where(QuestionAccess.PracticeFilter(access))
            .Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id, cancellationToken);
        if (questions.Count == 0) return UnprocessableEntity(new { Message = "No eligible related questions are available." });
        var session = new PracticeSession { Id = id, UserId = UserId, SourceMistakeId = mistake.Id };
        foreach (var candidate in candidates.Items)
        {
            if (!questions.TryGetValue(candidate.Id, out var question) ||
                question.QuestionCategoryId != candidate.QuestionCategoryId ||
                question.QuestionType != candidate.QuestionType || question.Content != candidate.Content) continue;
            session.Items.Add(new PracticeSessionItem
            {
                SessionId = id, Position = session.Items.Count, SourceQuestionId = question.Id,
                Content = question.Content, QuestionType = question.QuestionType,
                Metadata = question.Metadata, StandardAnswer = question.StandardAnswer,
                Explanation = question.Explanation, GradingStrategy = question.GradingStrategy
            });
        }
        if (session.Items.Count == 0) return UnprocessableEntity(new { Message = "Related questions changed. Please start again." });
        db.PracticeSessions.Add(session);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            // A simultaneous retry may have won the UUID. Never overwrite another owner or seed.
            db.ChangeTracker.Clear();
            existing = await Owned.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (existing == null)
            {
                if (await db.PracticeSessions.AnyAsync(s => s.Id == id, cancellationToken)) return Conflict();
                throw;
            }
            if (existing.SourceMistakeId != request.MistakeId) return Conflict();
            return Ok(await Project(existing, cancellationToken));
        }
        return StatusCode(StatusCodes.Status201Created, await Project(session, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken cancellationToken)
    {
        if (UserId == null) return Unauthorized();
        var session = await Owned.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return session == null ? NotFound() : Ok(await Project(session, cancellationToken));
    }

    /// <summary>One answer per item. Repeating it returns the committed result; changing it conflicts.</summary>
    [HttpPost("{id:guid}/items/{itemId:guid}/answer")]
    public async Task<IActionResult> Answer(Guid id, Guid itemId, PracticeSessionAnswerDto request,
        CancellationToken cancellationToken)
    {
        if (UserId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.UserAnswer)) return BadRequest();
        var session = await Owned.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        var item = session?.Items.SingleOrDefault(i => i.Id == itemId);
        if (item == null) return NotFound();
        if (item.SubmittedAt.HasValue) return item.UserAnswer == request.UserAnswer
            ? Ok(await Project(session!, cancellationToken)) : Conflict();
        var question = await db.Questions.AsNoTracking().Include(q => q.QuestionCategory)
            .SingleOrDefaultAsync(q => q.Id == item.SourceQuestionId, cancellationToken);
        if (question?.QuestionCategory == null) return NotFound();
        var access = await accessService.GetUserAccessStatusAsync(UserId);
        if (!QuestionAccess.CanPractice(question.QuestionCategory, access)) return StatusCode(StatusCodes.Status403Forbidden);

        // The conditional write holds this row until grading and all local history commit.
        // Concurrent requests cannot grade it again. Rollback leaves it retryable, including
        // a process crash: this guarantees one committed result, not exactly-once external AI billing.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var submittedAt = DateTime.UtcNow;
        var claimed = await db.PracticeSessionItems.Where(i => i.Id == itemId && i.SessionId == id && i.SubmittedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.SubmittedAt, submittedAt)
                .SetProperty(i => i.UserAnswer, request.UserAnswer), cancellationToken);
        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            session = await Owned.SingleAsync(s => s.Id == id, cancellationToken);
            return session.Items.Single(i => i.Id == itemId).UserAnswer == request.UserAnswer
                ? Ok(await Project(session, cancellationToken)) : Conflict();
        }

        // Grade the fixed version the student saw, not a later edit to the live question.
        var captured = new Question
        {
            Id = item.SourceQuestionId, Content = item.Content, QuestionType = item.QuestionType,
            Metadata = item.Metadata, StandardAnswer = item.StandardAnswer,
            Explanation = item.Explanation, GradingStrategy = item.GradingStrategy,
            QuestionCategoryId = question.QuestionCategoryId
        };
        var result = await grading.GradeAsync(captured, request.UserAnswer);
        var tracked = await db.PracticeSessionItems.SingleAsync(i => i.Id == itemId);
        tracked.IsCorrect = result.IsCorrect; tracked.Score = result.Score; tracked.GradingComment = result.Comment;
        db.UserPracticeHistories.Add(new UserPracticeHistory
        {
            Id = Guid.NewGuid(), UserId = UserId, QuestionId = question.Id, UserAnswer = request.UserAnswer,
            IsCorrect = result.IsCorrect, Score = result.Score, GradingComment = result.Comment, CreationTime = submittedAt
        });
        MistakeRecording.Record(db, UserId, captured, request.UserAnswer, result, null, submittedAt);
        // Finish persistence even if the HTTP client disconnects after grading. A later GET/retry
        // discovers the same committed result instead of charging a second grading attempt.
        await db.SaveChangesAsync(CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        session = await Owned.SingleAsync(s => s.Id == id);
        return Ok(await Project(session, CancellationToken.None));
    }

    private async Task<PracticeSessionDto> Project(PracticeSession session, CancellationToken cancellationToken)
    {
        var access = await accessService.GetUserAccessStatusAsync(session.UserId);
        var ids = session.Items.Select(i => i.SourceQuestionId).ToArray();
        var current = await db.Questions.AsNoTracking().Where(q => ids.Contains(q.Id))
            .Select(q => new { q.Id, q.QuestionCategory }).ToDictionaryAsync(q => q.Id, cancellationToken);
        var items = session.Items.OrderBy(i => i.Position).Select(i =>
        {
            var submitted = i.SubmittedAt.HasValue;
            var exists = current.TryGetValue(i.SourceQuestionId, out var question) && question.QuestionCategory != null;
            var allowed = exists && QuestionAccess.CanPractice(question!.QuestionCategory!, access);
            var availability = submitted ? "Answered" : !exists ? "QuestionUnavailable" : allowed ? "Ready" : "MembershipRequired";
            return new PracticeSessionItemDto(i.Id, i.Position, i.SourceQuestionId, availability,
                submitted || allowed ? i.Content : null, i.QuestionType, submitted || allowed ? i.Metadata : null,
                i.SubmittedAt, i.UserAnswer, i.IsCorrect, i.Score, submitted ? i.StandardAnswer : null,
                submitted ? i.Explanation : null, i.GradingComment);
        }).ToArray();
        var count = items.Count(i => i.SubmittedAt.HasValue);
        return new(session.Id, session.SourceMistakeId, session.CreationTime, count, count == items.Length, items);
    }
}
