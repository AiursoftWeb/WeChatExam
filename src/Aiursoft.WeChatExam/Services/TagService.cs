using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

public class TagService : ITagService
{
    private readonly WeChatExamDbContext _dbContext;
    private readonly IStringLocalizer<TagService> _localizer;

    public TagService(WeChatExamDbContext dbContext, IStringLocalizer<TagService> localizer)
    {
        _dbContext = dbContext;
        _localizer = localizer;
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

        // A cached old name still identifies the same retained tag, never a new source tag.
        var oldCollectionId = await _dbContext.LegacyPaperCollectionAliases.Where(a => a.NormalizedName == normalizedName)
            .Select(a => (Guid?)a.CollectionId).SingleOrDefaultAsync();
        if (oldCollectionId.HasValue)
        {
            var oldTagId = await _dbContext.LegacyPaperCollectionBindings.Where(b => b.CollectionId == oldCollectionId).Select(b => b.TagId).SingleAsync();
            return await _dbContext.Tags.Include(t => t.Taxonomy).SingleAsync(t => t.Id == oldTagId);
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

    public async Task CreateTagAsync(Tag tag)
    {
        if (tag.DisplayName.Length > 255)
        {
            tag.DisplayName = tag.DisplayName.Substring(0, 255);
        }
        if (tag.NormalizedName.Length > 255)
        {
            tag.NormalizedName = tag.NormalizedName.Substring(0, 255);
        }
        await EnsureNameNotReservedAsync(PaperCollectionService.Normalize(tag.DisplayName));
        _dbContext.Tags.Add(tag);
        await _dbContext.SaveChangesAsync();
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

    public async Task AddTagToPaperAsync(Guid paperId, int tagId)
    {
        await EnsureNotCollectionTagAsync(tagId);

        var exists = await _dbContext.PaperTags
            .AnyAsync(pt => pt.PaperId == paperId && pt.TagId == tagId);

        if (exists) return;

        var paperTag = new PaperTag
        {
            PaperId = paperId,
            TagId = tagId
        };

        _dbContext.PaperTags.Add(paperTag);
        await _dbContext.SaveChangesAsync();
    }

    public async Task RemoveTagFromPaperAsync(Guid paperId, int tagId)
    {
        await EnsureNotCollectionTagAsync(tagId);

        var paperTag = await _dbContext.PaperTags
            .FirstOrDefaultAsync(pt => pt.PaperId == paperId && pt.TagId == tagId);

        if (paperTag != null)
        {
            _dbContext.PaperTags.Remove(paperTag);
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<Tag>> GetTagsForPaperAsync(Guid paperId)
    {
        return await _dbContext.PaperTags
            .Where(pt => pt.PaperId == paperId)
            .Include(pt => pt.Tag)
            .Select(pt => pt.Tag)
            .ToListAsync();
    }

    public async Task<List<Paper>> GetPapersByTagAsync(int tagId)
    {
        return await _dbContext.PaperTags
            .Where(pt => pt.TagId == tagId)
            .Include(pt => pt.Paper)
            .Select(pt => pt.Paper)
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
            .OrderBy(t => t.OrderIndex)
            .ThenBy(t => t.DisplayName)
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
        await EnsureNotCollectionTagAsync(tagId);

        // First remove all question-tag relationships
        var questionTags = await _dbContext.QuestionTags
            .Where(qt => qt.TagId == tagId)
            .ToListAsync();

        _dbContext.QuestionTags.RemoveRange(questionTags);

        // Remove all paper-tag relationships
        var paperTags = await _dbContext.PaperTags
            .Where(pt => pt.TagId == tagId)
            .ToListAsync();

        _dbContext.PaperTags.RemoveRange(paperTags);

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
        var binding = await _dbContext.LegacyPaperCollectionBindings.AsNoTracking().Include(b => b.Tag).SingleOrDefaultAsync(b => b.TagId == tag.Id);
        if (binding != null && (tag.DisplayName != binding.Tag.DisplayName || tag.NormalizedName != binding.Tag.NormalizedName
            || tag.TaxonomyId != binding.Tag.TaxonomyId || tag.OrderIndex != binding.Tag.OrderIndex))
            throw new InvalidOperationException(_localizer["Edit directory names and order through the paper collection editor."]);
        if (tag.DisplayName.Length > 255)
        {
            tag.DisplayName = tag.DisplayName.Substring(0, 255);
        }
        if (tag.NormalizedName.Length > 255)
        {
            tag.NormalizedName = tag.NormalizedName.Substring(0, 255);
        }
        if (binding == null) await EnsureNameNotReservedAsync(PaperCollectionService.Normalize(tag.DisplayName));
        _dbContext.Tags.Update(tag);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<List<Tag>> GetTagsByTaxonomyIdAsync(int taxonomyId)
    {
        return await _dbContext.Tags
            .Where(t => t.TaxonomyId == taxonomyId)
            .Include(t => t.Taxonomy)
            .ThenInclude(taxonomy => taxonomy!.CategoryTaxonomies)
            .OrderBy(t => t.OrderIndex)
            .ThenBy(t => t.DisplayName)
            .ToListAsync();
    }

    private async Task EnsureNotCollectionTagAsync(int tagId)
    {
        if (await _dbContext.LegacyPaperCollectionBindings.AnyAsync(b => b.TagId == tagId))
            throw new InvalidOperationException(_localizer["This tag belongs to a paper collection. Use the collection editor to manage its directory."]);
    }

    private async Task EnsureNameNotReservedAsync(string name)
    {
        if (await _dbContext.LegacyPaperCollectionAliases.AnyAsync(a => a.NormalizedName == name))
            throw new InvalidOperationException(_localizer["This name is reserved for an existing or renamed paper collection."]);
    }
}
