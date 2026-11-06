using System.Data;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>Shared editor checks. Historical payloads are never overwritten or removed.</summary>
public class PaperPublication(WeChatExamDbContext context)
{
    public static bool NeedsAnswer(QuestionSnapshot question) =>
        string.IsNullOrWhiteSpace(question.StandardAnswer) ||
        string.Equals(question.Content.Trim(), question.StandardAnswer.Trim(), StringComparison.Ordinal);

    public static bool SameContent(PaperSnapshot current, PaperSnapshot saved) =>
        current.Title == saved.Title && current.TimeLimit == saved.TimeLimit && current.IsFree == saved.IsFree &&
        current.QuestionSnapshots.OrderBy(q => q.Order).ThenBy(q => q.OriginalQuestionId)
            .Select(Payload).SequenceEqual(saved.QuestionSnapshots.OrderBy(q => q.Order).ThenBy(q => q.OriginalQuestionId).Select(Payload));

    private static object Payload(QuestionSnapshot q) => new
    {
        q.OriginalQuestionId, q.Order, q.Score, q.Content, q.QuestionType, q.GradingStrategy,
        q.Metadata, q.StandardAnswer, q.Explanation
    };

    public async Task<PaperPublicationViewModel?> ReadAsync(Guid id, bool real)
    {
        string title;
        int timeLimit;
        bool free;
        PaperStatus status;
        List<QuestionSnapshot> questions;
        if (real)
        {
            var paper = await context.RealPapers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
            if (paper == null) return null;
            title = paper.Title; timeLimit = paper.TimeLimit; free = false; status = paper.Status;
            questions = await context.RealPaperQuestions.AsNoTracking().Where(l => l.RealPaperId == id)
                .OrderBy(l => l.Order).ThenBy(l => l.QuestionId).Select(l => new QuestionSnapshot
                {
                    PaperSnapshotId = Guid.Empty, OriginalQuestionId = l.QuestionId, Order = l.Order, Score = l.Score,
                    Content = l.Question.Content, QuestionType = l.Question.QuestionType, GradingStrategy = l.Question.GradingStrategy,
                    Metadata = l.Question.Metadata, StandardAnswer = l.Question.StandardAnswer, Explanation = l.Question.Explanation
                }).ToListAsync();
        }
        else
        {
            var paper = await context.MockPapers.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
            if (paper == null) return null;
            title = paper.Title; timeLimit = paper.TimeLimit; free = paper.AllowViewAnonymousely; status = paper.Status;
            questions = await context.MockPaperQuestions.AsNoTracking().Where(l => l.MockPaperId == id)
                .OrderBy(l => l.Order).ThenBy(l => l.QuestionId).Select(l => new QuestionSnapshot
                {
                    PaperSnapshotId = Guid.Empty, OriginalQuestionId = l.QuestionId, Order = l.Order, Score = l.Score,
                    Content = l.Question.Content, QuestionType = l.Question.QuestionType, GradingStrategy = l.Question.GradingStrategy,
                    Metadata = l.Question.Metadata, StandardAnswer = l.Question.StandardAnswer, Explanation = l.Question.Explanation
                }).ToListAsync();
        }
        return new PaperPublicationViewModel
        {
            Id = id, IsReal = real, Title = title, Status = status,
            Latest = await context.PaperSnapshots.AsNoTracking().Include(s => s.QuestionSnapshots)
                .Where(s => real ? s.RealPaperId == id : s.MockPaperId == id).OrderByDescending(s => s.Version).FirstOrDefaultAsync(),
            Current = new PaperSnapshot { Id = Guid.Empty, Title = title, TimeLimit = timeLimit, IsFree = free, QuestionSnapshots = questions }
        };
    }

    public Task UnpublishAsync(Guid id, bool real) => context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        context.ChangeTracker.Clear();
        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        if (real)
        {
            var paper = await context.RealPapers.SingleAsync(p => p.Id == id);
            if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("This paper is read only.");
            paper.Status = PaperStatus.Draft;
        }
        else
        {
            var paper = await context.MockPapers.SingleAsync(p => p.Id == id);
            if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("This paper is read only.");
            paper.Status = PaperStatus.Draft;
        }
        await context.SaveChangesAsync();
        if (transaction != null) await transaction.CommitAsync();
    });
}
