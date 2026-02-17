using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class TagService : ITagService
{
    private readonly WeChatExamDbContext _dbContext;

    public TagService(WeChatExamDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Tag> AddTagAsync(string displayName)
    {
        return await GetOrCreateTagAsync(displayName);
    }

    public async Task<Tag> GetOrCreateTagAsync(string displayName, int? taxonomyId = null)
    {
        if (displayName.Length > 255)
        {
            displayName = displayName.Substring(0, 255);
        }

        var normalizedName = displayName.Trim().ToUpperInvariant();
        if (normalizedName.Length > 255)
        {
            normalizedName = normalizedName.Substring(0, 255);
        }

        var existingTag = await _dbContext.Tags
            .Include(t => t.Taxonomy)
            .FirstOrDefaultAsync(t => t.NormalizedName == normalizedName);

        if (existingTag != null)
        {
            if (taxonomyId.HasValue && existingTag.TaxonomyId == null)
            {
                existingTag.TaxonomyId = taxonomyId.Value;
                await _dbContext.SaveChangesAsync();
            }
            return existingTag;
        }

        var newTag = new Tag
        {
            DisplayName = displayName,
            NormalizedName = normalizedName,
            TaxonomyId = taxonomyId
        };

        _dbContext.Tags.Add(newTag);
        await _dbContext.SaveChangesAsync();
        return newTag;
    }

    public async Task<List<Tag>> GetAllTagsAsync()
    {
        return await _dbContext.Tags
            .Include(t => t.Taxonomy)
            .ToListAsync();
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

    public async Task<List<Tag>> SearchTagsAsync(string? query, int? taxonomyId = null)
    {
        var dbQuery = _dbContext.Tags.Include(t => t.Taxonomy).AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = query.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(t => t.NormalizedName.Contains(normalizedQuery));
        }

        if (taxonomyId.HasValue)
        {
            if (taxonomyId.Value == 0)
            {
                dbQuery = dbQuery.Where(t => t.TaxonomyId == null);
            }
            else
            {
                dbQuery = dbQuery.Where(t => t.TaxonomyId == taxonomyId.Value);
            }
        }

        return await dbQuery
            .OrderBy(t => t.DisplayName)
            .ToListAsync();
    }

    public async Task<Tag?> GetTagByIdAsync(int tagId)
    {
        return await _dbContext.Tags
            .Include(t => t.Taxonomy)
            .FirstOrDefaultAsync(t => t.Id == tagId);
    }

    public async Task DeleteTagAsync(int tagId)
    {
        // First remove all question-tag relationships
        var questionTags = await _dbContext.QuestionTags
            .Where(qt => qt.TagId == tagId)
            .ToListAsync();

        _dbContext.QuestionTags.RemoveRange(questionTags);

        // Then remove the tag itself
        var tag = await _dbContext.Tags.FindAsync(tagId);
        if (tag != null)
        {
            _dbContext.Tags.Remove(tag);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateTagAsync(Tag tag)
    {
        if (tag.DisplayName.Length > 255)
        {
            tag.DisplayName = tag.DisplayName.Substring(0, 255);
        }
        if (tag.NormalizedName.Length > 255)
        {
            tag.NormalizedName = tag.NormalizedName.Substring(0, 255);
        }
        _dbContext.Tags.Update(tag);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<Tag>> GetTagsByTaxonomyIdAsync(int taxonomyId)
    {
        return await _dbContext.Tags
            .Where(t => t.TaxonomyId == taxonomyId)
            .Include(t => t.Taxonomy)
            .OrderBy(t => t.DisplayName)
            .ToListAsync();
    }
}
