using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class TagService : ITagService
{
    private readonly TemplateDbContext _dbContext;

    public TagService(TemplateDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tag> AddTagAsync(string displayName)
    {
        var normalizedName = displayName.Trim().ToUpperInvariant();
        var existingTag = await _dbContext.Tags
            .FirstOrDefaultAsync(t => t.NormalizedName == normalizedName);

        if (existingTag != null)
        {
            return existingTag;
        }

        var newTag = new Tag
        {
            DisplayName = displayName,
            NormalizedName = normalizedName
        };

        _dbContext.Tags.Add(newTag);
        await _dbContext.SaveChangesAsync();
        return newTag;
    }

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        return await _dbContext.Tags.ToListAsync();
    }

    public async Task AddTagToQuestionAsync(Guid questionId, int tagId)
    {
        var exists = await _dbContext.QuestionTags
            .AnyAsync(qt => qt.QuestionId == questionId && qt.TagId == tagId);

        if (exists) return;

        var questionTag = new QuestionTag
        {
            QuestionId = questionId,
            TagId = tagId
        };

        _dbContext.QuestionTags.Add(questionTag);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveTagFromQuestionAsync(Guid questionId, int tagId)
    {
        var questionTag = await _dbContext.QuestionTags
            .FirstOrDefaultAsync(qt => qt.QuestionId == questionId && qt.TagId == tagId);

        if (questionTag != null)
        {
            _dbContext.QuestionTags.Remove(questionTag);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<Tag>> GetTagsForQuestionAsync(Guid questionId)
    {
        return await _dbContext.QuestionTags
            .Where(qt => qt.QuestionId == questionId)
            .Include(qt => qt.Tag)
            .Select(qt => qt.Tag)
            .ToListAsync();
    }

    public async Task<List<Question>> GetQuestionsByTagAsync(int tagId)
    {
        return await _dbContext.QuestionTags
            .Where(qt => qt.TagId == tagId)
            .Include(qt => qt.Question)
            .Select(qt => qt.Question)
            .ToListAsync();
    }
}
