using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface ITagService
{
    Task<Tag> AddTagAsync(string displayName);
    Task<List<Tag>> GetAllTagsAsync();
    Task AddTagToQuestionAsync(Guid questionId, int tagId);
    Task RemoveTagFromQuestionAsync(Guid questionId, int tagId);
    Task<List<Tag>> GetTagsForQuestionAsync(Guid questionId);
    Task<List<Question>> GetQuestionsByTagAsync(int tagId);
}

