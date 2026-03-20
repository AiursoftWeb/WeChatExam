using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface ITaxonomyService
{
    Task<Taxonomy> AddTaxonomyAsync(string name, Guid[]? categoryIds = null);
    Task<List<Taxonomy>> GetAllTaxonomiesAsync(Guid? categoryId = null, bool includeCategory = false);
    Task<Taxonomy?> GetTaxonomyByIdAsync(int id);
    Task DeleteTaxonomyAsync(int id);
    Task UpdateTaxonomyAsync(Taxonomy taxonomy, Guid[]? categoryIds = null);
}
