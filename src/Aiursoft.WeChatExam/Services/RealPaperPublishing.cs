using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Publishes a fixed exam payload. Later template or question edits never update that payload.
/// No legacy Paper row is created: only the snapshot format is shared with existing exams.
/// </summary>
public class RealPaperPublishing(WeChatExamDbContext context, IStringLocalizer<RealPaperPublishing> localizer)
{
    public async Task<PaperSnapshot> PublishAsync(RealPaper paper)
    {
        if (paper.Status is not (PaperStatus.Draft or PaperStatus.Publishable))
            throw new InvalidOperationException(localizer["This paper is read only."]);
        if (!await context.Institutions.AnyAsync(i => i.Id == paper.InstitutionId))
            throw new InvalidOperationException(localizer["Select an existing institution."]);
        var questions = await context.RealPaperQuestions.AsNoTracking()
            .Where(l => l.RealPaperId == paper.Id).Include(l => l.Question).ThenInclude(q => q.QuestionCategory)
            .OrderBy(l => l.Order).ThenBy(l => l.QuestionId).ToListAsync();
        if (questions.Count == 0)
            throw new InvalidOperationException(localizer["Add at least one question before publishing."]);
        // Real papers intentionally span courses. Their institution entitlement covers the entire payload.
        if (questions.Any(l => l.Order < 0 || l.Score <= 0))
            throw new InvalidOperationException(localizer["Every question must have a nonnegative order and a positive score."]);
        if (string.IsNullOrWhiteSpace(paper.Title) || paper.TimeLimit <= 0)
            throw new InvalidOperationException(localizer["Set a title and a positive time limit before publishing."]);
        var version = await context.PaperSnapshots.Where(s => s.RealPaperId == paper.Id)
            .MaxAsync(s => (int?)s.Version) ?? 0;
        if (version == int.MaxValue)
            throw new InvalidOperationException(localizer["The maximum version number has been reached."]);
        var snapshotId = Guid.NewGuid();
        var snapshot = new PaperSnapshot
        {
            Id = snapshotId, RealPaperId = paper.Id, Version = version + 1,
            Title = paper.Title, TimeLimit = paper.TimeLimit, IsFree = false,
            QuestionSnapshots = questions.Select(l => new QuestionSnapshot
            {
                Id = Guid.NewGuid(), PaperSnapshotId = snapshotId, OriginalQuestionId = l.QuestionId,
                Order = l.Order, Score = l.Score, Content = l.Question.Content,
                QuestionType = l.Question.QuestionType, GradingStrategy = l.Question.GradingStrategy,
                Metadata = l.Question.Metadata, StandardAnswer = l.Question.StandardAnswer,
                Explanation = l.Question.Explanation
            }).ToList()
        };
        context.PaperSnapshots.Add(snapshot);
        paper.Status = PaperStatus.Publishable;
        // One save keeps the version, all question copies and template status atomic.
        // The unique source/version index rejects concurrent duplicate versions.
        await context.SaveChangesAsync();
        return snapshot;
    }
}
