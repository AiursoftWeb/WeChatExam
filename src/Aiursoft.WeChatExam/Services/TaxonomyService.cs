using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

// Taxonomies organize internal tags. Courses and student navigation have independent entities.
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

    public Task<List<Taxonomy>> GetAllTaxonomiesAsync() =>
        dbContext.Taxonomies.OrderBy(t => t.Id).ToListAsync();

    public Task<Taxonomy?> GetTaxonomyByIdAsync(int id) =>
        dbContext.Taxonomies.FirstOrDefaultAsync(t => t.Id == id);

    public async Task DeleteTaxonomyAsync(int id)
    {
        var taxonomy = await dbContext.Taxonomies.FindAsync(id);
        if (taxonomy == null) return;

        // Keep the tags and their content associations when removing their grouping.
        var tags = await dbContext.Tags.Where(t => t.TaxonomyId == id).ToListAsync();
        foreach (var tag in tags) tag.TaxonomyId = null;
        dbContext.Taxonomies.Remove(taxonomy);
        await dbContext.SaveChangesAsync();
    }

    public async Task UpdateTaxonomyAsync(Taxonomy taxonomy)
    {
        dbContext.Taxonomies.Update(taxonomy);
        await dbContext.SaveChangesAsync();
    }
}
