using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// The only directory writer. New memberships and their old-client projection commit together.
/// Neither importing a directory nor moving a paper changes content, publication or VIP rules.
/// </summary>
public class PaperCollectionService(WeChatExamDbContext db, IStringLocalizer<PaperCollectionService> localizer)
{
    public static IQueryable<Paper> Available(IQueryable<Paper> query) => query.Where(p =>
        (p.Status == PaperStatus.Publishable || p.Status == PaperStatus.Frozen) && p.PaperSnapshots.Any());

    public async Task<CollectionImportPreview> PreviewAsync(int taxonomyId)
    {
        var taxonomy = await db.Taxonomies.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taxonomyId)
            ?? throw new InvalidOperationException(localizer["The selected taxonomy no longer exists."]);
        var tags = await db.Tags.AsNoTracking().Where(t => t.TaxonomyId == taxonomyId)
            .OrderBy(t => t.Id).Select(t => new CollectionImportTag(t.Id, t.DisplayName, t.IsFree, t.OrderIndex,
                t.QuestionTags.Count(), t.PaperTags.Select(p => p.PaperId).OrderBy(id => id).ToList())).ToListAsync();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { taxonomyId, tags }))));
        return new CollectionImportPreview(taxonomyId, taxonomy.Name, fingerprint, tags);
    }

    public async Task ImportAsync(int taxonomyId, string fingerprint)
    {
        await ExecuteAtomicAsync(async () =>
        {
            if (await db.LegacyPaperCollectionCatalogs.AnyAsync())
                throw new InvalidOperationException(localizer["An exam paper directory has already been connected. Reload the collection list."]);
            var preview = await PreviewAsync(taxonomyId);
            if (preview.Fingerprint != fingerprint)
                throw new InvalidOperationException(localizer["The source data changed. Preview the directory again before connecting it."]);
            var tags = await db.Tags.Where(t => t.TaxonomyId == taxonomyId).OrderBy(t => t.Id).ToListAsync();
            if (tags.Select(t => Normalize(t.DisplayName)).Distinct().Count() != tags.Count)
                throw new InvalidOperationException(localizer["Duplicate directory names must be resolved before connecting this taxonomy."]);
            var sourceIds = tags.Select(t => t.Id).ToArray();
            var sourceNames = tags.Select(t => Normalize(t.DisplayName)).ToArray();
            if (await db.Tags.AnyAsync(t => !sourceIds.Contains(t.Id) && sourceNames.Contains(t.NormalizedName)))
                throw new InvalidOperationException(localizer["A directory name is also used by another tag. Resolve the duplicate before connecting it."]);
            db.LegacyPaperCollectionCatalogs.Add(new LegacyPaperCollectionCatalog { TaxonomyId = taxonomyId });
            foreach (var tag in tags)
            {
                // Retain the tag ID, fee, taxonomy, questions and existing paper links exactly as they are.
                var collection = new PaperCollection { Name = tag.DisplayName, OrderIndex = tag.OrderIndex, IsVisible = true };
                db.PaperCollections.Add(collection);
                db.LegacyPaperCollectionBindings.Add(new LegacyPaperCollectionBinding { Collection = collection, CollectionId = collection.Id, Tag = tag, TagId = tag.Id });
                db.LegacyPaperCollectionAliases.Add(new LegacyPaperCollectionAlias { NormalizedName = Normalize(tag.DisplayName), CollectionId = collection.Id });
                foreach (var paperId in preview.Tags.Single(t => t.Id == tag.Id).PaperIds.Distinct())
                    db.PaperCollectionPapers.Add(new PaperCollectionPaper { CollectionId = collection.Id, PaperId = paperId });
            }
            await db.SaveChangesAsync();
        });
    }

    public async Task<Guid> SaveAsync(Guid? id, Guid revision, string name, int order, bool visible, bool archived, IEnumerable<Guid> paperIds)
    {
        name = name.Trim();
        if (name.Length is 0 or > 255) throw new InvalidOperationException(localizer["A collection name must contain 1 to 255 characters."]);
        var ids = paperIds.Distinct().ToArray();
        var resultId = id ?? Guid.NewGuid();
        await ExecuteAtomicAsync(async () =>
        {
            var catalog = await db.LegacyPaperCollectionCatalogs.SingleOrDefaultAsync()
                ?? throw new InvalidOperationException(localizer["Connect the existing exam paper directory first."]);
            if (await db.Papers.CountAsync(p => ids.Contains(p.Id)) != ids.Length)
                throw new InvalidOperationException(localizer["Some selected exam papers no longer exist. Reload the editor."]);
            var normalized = Normalize(name);
            var binding = id.HasValue ? await db.LegacyPaperCollectionBindings.Include(b => b.Tag)
                .Include(b => b.Collection).SingleOrDefaultAsync(b => b.CollectionId == id) : null;
            if (id.HasValue && binding == null) throw new InvalidOperationException(localizer["The collection no longer exists."]);
            if (binding != null && binding.Collection.Revision != revision)
                throw new InvalidOperationException(localizer["This collection was changed in another window. Reload before saving."]);
            var existingTagId = binding?.TagId ?? -1;
            if (await db.LegacyPaperCollectionAliases.AnyAsync(a => a.NormalizedName == normalized && a.CollectionId != resultId)
                || await db.Tags.AnyAsync(t => (t.NormalizedName == normalized || t.DisplayName == name) && t.Id != existingTagId))
                throw new InvalidOperationException(localizer["This name is already used by a tag or an earlier collection name. Choose another name."]);
            if (binding == null)
            {
                var collection = new PaperCollection { Id = resultId, Name = name };
                // New compatibility tags are directory-only. PaperAccessService still guards paid content.
                var tag = new Tag { DisplayName = name, NormalizedName = normalized, TaxonomyId = catalog.TaxonomyId, IsFree = true };
                binding = new LegacyPaperCollectionBinding { CollectionId = resultId, Collection = collection, Tag = tag };
                db.PaperCollections.Add(collection);
                db.Tags.Add(tag);
                db.LegacyPaperCollectionBindings.Add(binding);
            }
            binding.Collection.Name = name;
            binding.Collection.OrderIndex = order;
            binding.Collection.IsVisible = visible;
            binding.Collection.IsArchived = archived;
            Touch(binding.Collection);
            binding.Tag.DisplayName = name;
            binding.Tag.NormalizedName = normalized;
            binding.Tag.OrderIndex = order;
            if (!await db.LegacyPaperCollectionAliases.AnyAsync(a => a.NormalizedName == normalized))
                db.LegacyPaperCollectionAliases.Add(new LegacyPaperCollectionAlias { NormalizedName = normalized, CollectionId = resultId });
            var current = await db.PaperCollectionPapers.Where(p => p.CollectionId == resultId).ToListAsync();
            db.PaperCollectionPapers.RemoveRange(current.Where(p => !ids.Contains(p.PaperId)));
            foreach (var paperId in ids.Where(p => current.All(c => c.PaperId != p)))
                db.PaperCollectionPapers.Add(new PaperCollectionPaper { CollectionId = resultId, PaperId = paperId });
            var oldLinks = id.HasValue ? await db.PaperTags.Where(p => p.TagId == binding.TagId).ToListAsync() : [];
            db.PaperTags.RemoveRange(oldLinks.Where(p => !ids.Contains(p.PaperId)));
            foreach (var paperId in ids.Where(p => oldLinks.All(c => c.PaperId != p)))
                db.PaperTags.Add(new PaperTag { PaperId = paperId, TagId = binding.TagId, Tag = binding.Tag });
            await db.SaveChangesAsync();
        });
        return resultId;
    }

    public async Task SetPaperCollectionsAsync(Guid paperId, IEnumerable<Guid> collectionIds)
    {
        await ExecuteAtomicAsync(async () =>
        {
            if (!await db.Papers.AnyAsync(p => p.Id == paperId))
                throw new InvalidOperationException(localizer["The exam paper no longer exists."]);
            await StagePaperCollectionsAsync(paperId, collectionIds);
            await db.SaveChangesAsync();
        });
    }

    public async Task<string> MembershipRevisionAsync(Guid paperId)
    {
        var state = await db.PaperCollectionPapers.AsNoTracking().Where(p => p.PaperId == paperId)
            .OrderBy(p => p.CollectionId).Select(p => new { p.CollectionId, p.Collection.Revision }).ToListAsync();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state))));
    }

    public async Task VerifyMembershipRevisionAsync(Guid paperId, string expected)
    {
        if (await MembershipRevisionAsync(paperId) != expected)
            throw new InvalidOperationException(localizer["This exam paper's collections changed in another window. Reload before saving."]);
    }

    // Import owns its transaction and calls this staging method before its final SaveChanges.
    public async Task StagePaperCollectionsAsync(Guid paperId, IEnumerable<Guid> collectionIds)
    {
        var ids = collectionIds.Distinct().ToArray();
        var bindings = await db.LegacyPaperCollectionBindings.Include(b => b.Collection).ToListAsync();
        if (ids.Any(id => bindings.All(b => b.CollectionId != id)))
            throw new InvalidOperationException(localizer["Some selected collections no longer exist. Reload the editor."]);
        var current = await db.PaperCollectionPapers.Where(p => p.PaperId == paperId).ToListAsync();
        var tagIds = bindings.Select(b => b.TagId).ToArray();
        var oldLinks = await db.PaperTags.Where(p => p.PaperId == paperId && tagIds.Contains(p.TagId)).ToListAsync();
        foreach (var binding in bindings)
        {
            var wanted = ids.Contains(binding.CollectionId);
            var link = current.SingleOrDefault(p => p.CollectionId == binding.CollectionId);
            var old = oldLinks.FirstOrDefault(p => p.TagId == binding.TagId);
            if (wanted && binding.Collection.IsArchived && link == null)
                throw new InvalidOperationException(localizer["Restore an archived collection before adding exam papers."]);
            if (wanted && link == null) db.PaperCollectionPapers.Add(new PaperCollectionPaper { CollectionId = binding.CollectionId, PaperId = paperId });
            if (!wanted && link != null) db.PaperCollectionPapers.Remove(link);
            if (wanted && old == null) db.PaperTags.Add(new PaperTag { PaperId = paperId, TagId = binding.TagId });
            if (!wanted) db.PaperTags.RemoveRange(oldLinks.Where(p => p.TagId == binding.TagId));
            if (wanted != (link != null) || wanted != (old != null)) Touch(binding.Collection);
        }
    }

    public async Task<List<Tag>?> LegacyTagsAsync(int taxonomyId)
    {
        if (!await db.LegacyPaperCollectionCatalogs.AnyAsync(c => c.TaxonomyId == taxonomyId)) return null;
        return await db.LegacyPaperCollectionBindings.Where(b => b.Collection.IsVisible && !b.Collection.IsArchived)
            .OrderBy(b => b.Collection.OrderIndex).ThenBy(b => b.Collection.Name).ThenBy(b => b.TagId)
            .Include(b => b.Tag).ThenInclude(t => t.Taxonomy).ThenInclude(t => t!.CategoryTaxonomies)
            .Select(b => b.Tag).ToListAsync();
    }

    public Task<PaperCollection?> ResolveLegacyNameAsync(string name) => db.LegacyPaperCollectionAliases
        .Where(a => a.NormalizedName == Normalize(name)).Select(a => a.Collection).SingleOrDefaultAsync();

    public static string Normalize(string name) => name.Trim().ToUpperInvariant();

    private static void Touch(PaperCollection collection)
    {
        collection.Revision = Guid.NewGuid();
        collection.UpdatedAt = DateTime.UtcNow;
    }

    public async Task ExecuteAtomicAsync(Func<Task> action)
    {
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
            await action();
            if (transaction != null) await transaction.CommitAsync();
        });
    }
}

public class CollectionImportTag(int id, string name, bool isFree, int orderIndex, int questionCount, List<Guid> paperIds)
{
    public int Id { get; set; } = id;
    public string Name { get; set; } = name;
    public bool IsFree { get; set; } = isFree;
    public int OrderIndex { get; set; } = orderIndex;
    public int QuestionCount { get; set; } = questionCount;
    public List<Guid> PaperIds { get; set; } = paperIds;
}
public class CollectionImportPreview(int taxonomyId, string name, string fingerprint, List<CollectionImportTag> tags)
{
    public int TaxonomyId { get; set; } = taxonomyId;
    public string Name { get; set; } = name;
    public string Fingerprint { get; set; } = fingerprint;
    public List<CollectionImportTag> Tags { get; set; } = tags;
}
