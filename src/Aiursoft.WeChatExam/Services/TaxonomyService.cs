using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class TaxonomyService(WeChatExamDbContext dbContext) : ITaxonomyService
{
    public async Task<Taxonomy> AddTaxonomyAsync(string name)
    {
        var existing = await dbContext.Taxonomies.FirstOrDefaultAsync(t => t.Name == name);
        if (existing != null) return existing;

        var taxonomy = new Taxonomy { Name = name };
        dbContext.Taxonomies.Add(taxonomy);
        await dbContext.SaveChangesAsync();
        return taxonomy;
    }

    public async Task<List<Taxonomy>> GetAllTaxonomiesAsync()
    {
        return await dbContext.Taxonomies.OrderBy(t => t.Id).ToListAsync();
    }

    public async Task<Taxonomy?> GetTaxonomyByIdAsync(int id)
    {
        return await dbContext.Taxonomies.FindAsync(id);
    }

    public async Task DeleteTaxonomyAsync(int id)
    {
        var taxonomy = await dbContext.Taxonomies.FindAsync(id);
        if (taxonomy != null)
        {
            // Set Tags' TaxonomyId to null before deleting (or cascade delete if configured, but let's be safe)
            // Actually, if we delete a taxonomy, we probably want tags to become uncategorized.
            var tags = await dbContext.Tags.Where(t => t.TaxonomyId == id).ToListAsync();
            foreach (var tag in tags)
            {
                tag.TaxonomyId = null;
            }
            
            dbContext.Taxonomies.Remove(taxonomy);
            await dbContext.SaveChangesAsync();
        }
    }

    public async Task UpdateTaxonomyAsync(Taxonomy taxonomy)
    {
        dbContext.Taxonomies.Update(taxonomy);
        await dbContext.SaveChangesAsync();
    }
}
