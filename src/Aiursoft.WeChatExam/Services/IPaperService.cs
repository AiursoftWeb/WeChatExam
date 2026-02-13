using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IPaperService
{
    // Paper CRUD
    Task<Paper> CreatePaperAsync(string title, int timeLimit, bool isFree);
    Task<Paper?> GetPaperAsync(Guid paperId);
    Task<List<Paper>> GetAllPapersAsync();
    Task UpdatePaperAsync(Guid paperId, string title, int timeLimit, bool isFree);
    Task DeletePaperAsync(Guid paperId);

    // Question management
    Task AddQuestionToPaperAsync(Guid paperId, Guid questionId, int order, int score);
    Task AddQuestionsToPaperAsync(Guid paperId, IEnumerable<Guid> questionIds, int startingOrder, int score);
    Task RemoveQuestionFromPaperAsync(Guid paperId, Guid questionId);
    Task UpdateQuestionInPaperAsync(Guid paperId, Guid questionId, int order, int score);
    Task<List<PaperQuestion>> GetQuestionsForPaperAsync(Guid paperId);

    // Category management
    Task AssociateCategoryAsync(Guid paperId, Guid categoryId);
    Task RemoveCategoryAssociationAsync(Guid paperId, Guid categoryId);
    Task<List<Category>> GetCategoriesForPaperAsync(Guid paperId);
    Task ClearCategoriesForPaperAsync(Guid paperId);

    // State management
    Task SetStatusAsync(Guid paperId, PaperStatus newStatus);
    
    // Publishing and Snapshots
    Task<PaperSnapshot> PublishAsync(Guid paperId);
    Task<PaperSnapshot> FreezeAsync(Guid paperId);
    Task<PaperSnapshot?> GetSnapshotAsync(Guid snapshotId);
    Task<List<PaperSnapshot>> GetSnapshotsForPaperAsync(Guid paperId);

    // Import
    Task<Paper> ImportPaperAsync(string content, bool saveToQuestionBank, Guid categoryId);
}
