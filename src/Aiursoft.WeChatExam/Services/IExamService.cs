using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IExamService
{
    // Admin & Management
    Task<Exam> CreateExamAsync(string title, Guid paperId, DateTime startTime, DateTime endTime, int? durationMinutes = null);
    Task UpdateExamAsync(Guid examId, string title, DateTime startTime, DateTime endTime, int durationMinutes, bool isPublic, int allowedAttempts, bool allowReview, DateTime? showAnswerAfter);
    Task DeleteExamAsync(Guid examId);
    Task<Exam?> GetExamAsync(Guid examId);
    Task<List<Exam>> GetAllExamsAsync();
    
    // Student & Taking Exams
    Task<List<Exam>> GetAvailableExamsForUserAsync(string userId);
    Task<List<ExamRecord>> GetExamRecordsForUserAsync(string userId);
    Task<ExamRecord> StartExamAsync(Guid examId, string userId);
    Task SubmitAnswerAsync(Guid examRecordId, Guid questionSnapshotId, string answer);
    Task<ExamRecord> FinishExamAsync(Guid examRecordId);
    Task<ExamRecord?> GetExamRecordAsync(Guid recordId);
    
    // Grading
    Task UpdateScoreAsync(Guid examRecordId, int newScore, string comment);
}
