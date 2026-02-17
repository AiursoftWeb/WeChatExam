using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface ITagService
{
    Task<Tag> AddTagAsync(string displayName);
    Task<Tag> GetOrCreateTagAsync(string displayName, int? taxonomyId = null);
    Task<List<Tag>> GetAllTagsAsync();
    Task AddTagToQuestionAsync(Guid questionId, int tagId);
    Task RemoveTagFromQuestionAsync(Guid questionId, int tagId);
    Task<List<Tag>> GetTagsForQuestionAsync(Guid questionId);
    Task<List<Question>> GetQuestionsByTagAsync(int tagId);
    Task<List<Tag>> SearchTagsAsync(string? query, int? taxonomyId = null);
    Task<Tag?> GetTagByIdAsync(int tagId);
    Task DeleteTagAsync(int tagId);
    Task UpdateTagAsync(Tag tag);
    Task<List<Tag>> GetTagsByTaxonomyIdAsync(int taxonomyId);
}

