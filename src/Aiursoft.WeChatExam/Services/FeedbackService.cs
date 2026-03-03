using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.WeChatExam.Services;

public class FeedbackService : IFeedbackService, IScopedDependency
{
    private readonly WeChatExamDbContext _dbContext;

    public FeedbackService(WeChatExamDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Feedback> SubmitFeedbackAsync(string userId, string content, string? contact)
    {
        var feedback = new Feedback
        {
            UserId = userId,
            Content = content,
            Contact = contact,
            CreatedAt = DateTime.UtcNow,
            Status = FeedbackStatus.Pending
        };

        _dbContext.Feedbacks.Add(feedback);
        await _dbContext.SaveChangesAsync();
        return feedback;
    }

    public async Task<List<Feedback>> GetUserFeedbacksAsync(string userId)
    {
        return await _dbContext.Feedbacks
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<Feedback> items, int totalCount)> SearchFeedbacksAsync(int page, int pageSize, FeedbackStatus? status = null)
    {
        var query = _dbContext.Feedbacks
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(f => f.Status == status.Value);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Feedback?> GetFeedbackByIdAsync(int id)
    {
        return await _dbContext.Feedbacks
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task UpdateFeedbackStatusAsync(int id, FeedbackStatus status)
    {
        var feedback = await _dbContext.Feedbacks.FindAsync(id);
        if (feedback != null)
        {
            feedback.Status = status;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task DeleteFeedbackAsync(int id)
    {
        var feedback = await _dbContext.Feedbacks.FindAsync(id);
        if (feedback != null)
        {
            _dbContext.Feedbacks.Remove(feedback);
            await _dbContext.SaveChangesAsync();
        }
    }
}
