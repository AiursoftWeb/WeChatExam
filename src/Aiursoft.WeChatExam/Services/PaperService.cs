using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class PaperService : IPaperService
{
    private readonly WeChatExamDbContext _dbContext;

    public PaperService(WeChatExamDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    #region Paper CRUD

    public async Task<Paper> CreatePaperAsync(string title, int timeLimit, bool isFree)
    {
        var paper = new Paper
        {
            Id = Guid.NewGuid(),
            Title = title,
            TimeLimit = timeLimit,
            IsFree = isFree,
            Status = PaperStatus.Draft
        };

        _dbContext.Papers.Add(paper);
        await _dbContext.SaveChangesAsync();
        return paper;
    }

    public async Task<Paper?> GetPaperAsync(Guid paperId)
    {
        return await _dbContext.Papers
            .Include(p => p.PaperQuestions)
            .ThenInclude(pq => pq.Question)
            .FirstOrDefaultAsync(p => p.Id == paperId);
    }

    public async Task<List<Paper>> GetAllPapersAsync()
    {
        return await _dbContext.Papers
            .OrderByDescending(p => p.CreationTime)
            .ToListAsync();
    }

    public async Task UpdatePaperAsync(Guid paperId, string title, int timeLimit, bool isFree)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("Cannot modify frozen paper");

        paper.Title = title;
        paper.TimeLimit = timeLimit;
        paper.IsFree = isFree;

        _dbContext.Papers.Update(paper);
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeletePaperAsync(Guid paperId)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) return;

        // Delete associated PaperQuestions
        var paperQuestions = await _dbContext.PaperQuestions
            .Where(pq => pq.PaperId == paperId)
            .ToListAsync();
        _dbContext.PaperQuestions.RemoveRange(paperQuestions);

        _dbContext.Papers.Remove(paper);
        await _dbContext.SaveChangesAsync();
    }

    #endregion

    #region Question Management

    public async Task AddQuestionToPaperAsync(Guid paperId, Guid questionId, int order, int score)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("Cannot modify frozen paper");

        var exists = await _dbContext.PaperQuestions
            .AnyAsync(pq => pq.PaperId == paperId && pq.QuestionId == questionId);
        if (exists) throw new InvalidOperationException("Question already in paper");

        var paperQuestion = new PaperQuestion
        {
            PaperId = paperId,
            QuestionId = questionId,
            Order = order,
            Score = score
        };

        _dbContext.PaperQuestions.Add(paperQuestion);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveQuestionFromPaperAsync(Guid paperId, Guid questionId)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("Cannot modify frozen paper");

        var paperQuestion = await _dbContext.PaperQuestions
            .FirstOrDefaultAsync(pq => pq.PaperId == paperId && pq.QuestionId == questionId);
        
        if (paperQuestion != null)
        {
            _dbContext.PaperQuestions.Remove(paperQuestion);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateQuestionInPaperAsync(Guid paperId, Guid questionId, int order, int score)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status == PaperStatus.Frozen) throw new InvalidOperationException("Cannot modify frozen paper");

        var paperQuestion = await _dbContext.PaperQuestions
            .FirstOrDefaultAsync(pq => pq.PaperId == paperId && pq.QuestionId == questionId);
        
        if (paperQuestion == null) throw new InvalidOperationException("Question not in paper");

        paperQuestion.Order = order;
        paperQuestion.Score = score;

        _dbContext.PaperQuestions.Update(paperQuestion);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<PaperQuestion>> GetQuestionsForPaperAsync(Guid paperId)
    {
        return await _dbContext.PaperQuestions
            .Where(pq => pq.PaperId == paperId)
            .Include(pq => pq.Question)
            .OrderBy(pq => pq.Order)
            .ToListAsync();
    }

    #endregion

    #region State Management

    public async Task SetStatusAsync(Guid paperId, PaperStatus newStatus)
    {
        var paper = await _dbContext.Papers.FindAsync(paperId);
        if (paper == null) throw new InvalidOperationException("Paper not found");

        // Validate state transitions
        var currentStatus = paper.Status;
        var isValidTransition = (currentStatus, newStatus) switch
        {
            (PaperStatus.Draft, PaperStatus.Publishable) => true,
            (PaperStatus.Publishable, PaperStatus.Draft) => true,
            (PaperStatus.Publishable, PaperStatus.Frozen) => true,
            _ => false
        };

        if (!isValidTransition)
        {
            throw new InvalidOperationException($"Invalid state transition from {currentStatus} to {newStatus}");
        }

        paper.Status = newStatus;
        _dbContext.Papers.Update(paper);
        await _dbContext.SaveChangesAsync();
    }

    #endregion

    #region Publishing and Snapshots

    public async Task<PaperSnapshot> PublishAsync(Guid paperId)
    {
        var paper = await _dbContext.Papers
            .Include(p => p.PaperQuestions)
            .ThenInclude(pq => pq.Question)
            .FirstOrDefaultAsync(p => p.Id == paperId);

        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status != PaperStatus.Publishable) 
            throw new InvalidOperationException("Only publishable papers can be published");

        // Get next version number
        var latestVersion = await _dbContext.PaperSnapshots
            .Where(ps => ps.PaperId == paperId)
            .MaxAsync(ps => (int?)ps.Version) ?? 0;

        var snapshot = await CreateSnapshotAsync(paper, latestVersion + 1);
        return snapshot;
    }

    public async Task<PaperSnapshot> FreezeAsync(Guid paperId)
    {
        var paper = await _dbContext.Papers
            .Include(p => p.PaperQuestions)
            .ThenInclude(pq => pq.Question)
            .FirstOrDefaultAsync(p => p.Id == paperId);

        if (paper == null) throw new InvalidOperationException("Paper not found");
        if (paper.Status != PaperStatus.Publishable) 
            throw new InvalidOperationException("Only publishable papers can be frozen");

        // Get next version number
        var latestVersion = await _dbContext.PaperSnapshots
            .Where(ps => ps.PaperId == paperId)
            .MaxAsync(ps => (int?)ps.Version) ?? 0;

        // Create final snapshot
        var snapshot = await CreateSnapshotAsync(paper, latestVersion + 1);

        // Update paper status to Frozen
        paper.Status = PaperStatus.Frozen;
        _dbContext.Papers.Update(paper);
        await _dbContext.SaveChangesAsync();

        return snapshot;
    }

    private async Task<PaperSnapshot> CreateSnapshotAsync(Paper paper, int version)
    {
        var snapshot = new PaperSnapshot
        {
            Id = Guid.NewGuid(),
            PaperId = paper.Id,
            Version = version,
            Title = paper.Title,
            TimeLimit = paper.TimeLimit,
            IsFree = paper.IsFree
        };

        _dbContext.PaperSnapshots.Add(snapshot);

        // Copy all questions to snapshot
        foreach (var pq in paper.PaperQuestions)
        {
            var question = pq.Question;

            var questionSnapshot = new QuestionSnapshot
            {
                Id = Guid.NewGuid(),
                PaperSnapshotId = snapshot.Id,
                OriginalQuestionId = question.Id,
                Order = pq.Order,
                Score = pq.Score,
                Content = question.Content,
                QuestionType = question.QuestionType,
                GradingStrategy = question.GradingStrategy,
                Metadata = question.Metadata,
                StandardAnswer = question.StandardAnswer,
                Explanation = question.Explanation
            };

            _dbContext.QuestionSnapshots.Add(questionSnapshot);
        }

        await _dbContext.SaveChangesAsync();
        return snapshot;
    }

    public async Task<PaperSnapshot?> GetSnapshotAsync(Guid snapshotId)
    {
        return await _dbContext.PaperSnapshots
            .Include(ps => ps.QuestionSnapshots)
            .FirstOrDefaultAsync(ps => ps.Id == snapshotId);
    }

    public async Task<List<PaperSnapshot>> GetSnapshotsForPaperAsync(Guid paperId)
    {
        return await _dbContext.PaperSnapshots
            .Where(ps => ps.PaperId == paperId)
            .OrderByDescending(ps => ps.Version)
            .ToListAsync();
    }

    #endregion

    #region Import

    public async Task<Paper> ImportPaperAsync(string content, bool saveToQuestionBank, Guid categoryId)
    {
        // Parse content - assuming JSON format with title and questions array
        // This is a simplified implementation; real implementation would parse specific format
        
        // Create paper
        var paper = new Paper
        {
            Id = Guid.NewGuid(),
            Title = "Imported Paper",
            TimeLimit = 60,
            IsFree = false,
            Status = PaperStatus.Draft
        };

        _dbContext.Papers.Add(paper);
        await _dbContext.SaveChangesAsync();

        // TODO: Parse content and extract questions
        // For each question:
        // 1. If saveToQuestionBank, create Question entity
        // 2. Create PaperQuestion linking to paper

        return paper;
    }

    #endregion
}
