using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface ITaxonomyService
{
    Task<Taxonomy> AddTaxonomyAsync(string name);
    Task<List<Taxonomy>> GetAllTaxonomiesAsync();
    Task<Taxonomy?> GetTaxonomyByIdAsync(int id);
    Task DeleteTaxonomyAsync(int id);
    Task UpdateTaxonomyAsync(Taxonomy taxonomy);
}
