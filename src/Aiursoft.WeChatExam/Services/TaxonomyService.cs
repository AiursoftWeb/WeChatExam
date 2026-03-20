using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class TaxonomyService(WeChatExamDbContext dbContext) : ITaxonomyService
{
    public async Task<Taxonomy> AddTaxonomyAsync(string name, Guid[]? categoryIds = null)
    {
        var existing = await dbContext.Taxonomies
            .Include(t => t.CategoryTaxonomies)
            .FirstOrDefaultAsync(t => t.Name == name);
        
        if (existing != null)
        {
            if (categoryIds != null)
            {
                foreach (var categoryId in categoryIds)
                {
                    if (existing.CategoryTaxonomies.All(ct => ct.CategoryId != categoryId))
                    {
                        dbContext.CategoryTaxonomies.Add(new CategoryTaxonomy
                        {
                            TaxonomyId = existing.Id,
                            CategoryId = categoryId
                        });
                    }
                }
                await dbContext.SaveChangesAsync();
            }
            return existing;
        }

        var taxonomy = new Taxonomy
        {
            Name = name
        };
        dbContext.Taxonomies.Add(taxonomy);
        await dbContext.SaveChangesAsync();

        if (categoryIds != null)
        {
            foreach (var categoryId in categoryIds)
            {
                dbContext.CategoryTaxonomies.Add(new CategoryTaxonomy
                {
                    TaxonomyId = taxonomy.Id,
                    CategoryId = categoryId
                });
            }
            await dbContext.SaveChangesAsync();
        }
        
        return taxonomy;
    }

    public async Task<List<Taxonomy>> GetAllTaxonomiesAsync(Guid? categoryId = null, bool includeCategory = false)
    {
        var query = dbContext.Taxonomies.AsQueryable();
        if (categoryId != null)
        {
            query = query.Where(t => t.CategoryTaxonomies.Any(ct => ct.CategoryId == categoryId));
        }
        if (includeCategory)
        {
            query = query.Include(t => t.CategoryTaxonomies)
                .ThenInclude(ct => ct.Category);
        }
        return await query.OrderBy(t => t.Id).ToListAsync();
    }

    public async Task<Taxonomy?> GetTaxonomyByIdAsync(int id)
    {
        return await dbContext.Taxonomies
            .Include(t => t.CategoryTaxonomies)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task DeleteTaxonomyAsync(int id)
    {
        var taxonomy = await dbContext.Taxonomies.FindAsync(id);
        if (taxonomy != null)
        {
            var tags = await dbContext.Tags.Where(t => t.TaxonomyId == id).ToListAsync();
            foreach (var tag in tags)
            {
                tag.TaxonomyId = null;
            }

            var links = await dbContext.CategoryTaxonomies.Where(ct => ct.TaxonomyId == id).ToListAsync();
            dbContext.CategoryTaxonomies.RemoveRange(links);
            
            dbContext.Taxonomies.Remove(taxonomy);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateTaxonomyAsync(Taxonomy taxonomy, Guid[]? categoryIds = null)
    {
        dbContext.Taxonomies.Update(taxonomy);

        if (categoryIds != null)
        {
            var currentLinks = await dbContext.CategoryTaxonomies
                .Where(ct => ct.TaxonomyId == taxonomy.Id)
                .ToListAsync();

            var toRemove = currentLinks.Where(cl => !categoryIds.Contains(cl.CategoryId)).ToList();
            var toAdd = categoryIds.Where(cid => currentLinks.All(cl => cl.CategoryId != cid))
                .Select(cid => new CategoryTaxonomy
                {
                    TaxonomyId = taxonomy.Id,
                    CategoryId = cid
                }).ToList();

            dbContext.CategoryTaxonomies.RemoveRange(toRemove);
            dbContext.CategoryTaxonomies.AddRange(toAdd);
        }

        await dbContext.SaveChangesAsync();
    }
}
