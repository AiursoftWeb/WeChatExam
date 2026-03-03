using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IFeedbackService
{
    Task<Feedback> SubmitFeedbackAsync(string userId, string content, string? contact);
    Task<List<Feedback>> GetUserFeedbacksAsync(string userId);
    Task<(List<Feedback> items, int totalCount)> SearchFeedbacksAsync(int page, int pageSize, FeedbackStatus? status = null);
    Task<Feedback?> GetFeedbackByIdAsync(int id);
    Task UpdateFeedbackStatusAsync(int id, FeedbackStatus status);
}
