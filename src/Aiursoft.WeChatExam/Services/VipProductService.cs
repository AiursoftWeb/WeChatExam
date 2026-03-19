using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// VIP 商品管理服务实现
/// </summary>
public class VipProductService(WeChatExamDbContext dbContext) : IVipProductService
{
    public async Task<List<VipProduct>> GetAllAsync()
    {
        return await dbContext.VipProducts
            .Include(v => v.Category)
            .OrderByDescending(v => v.CreationTime)
            .ToListAsync();
    }

    public async Task<VipProduct?> GetByIdAsync(Guid id)
    {
        return await dbContext.VipProducts
            .Include(v => v.Category)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<VipProduct> CreateAsync(string name, VipProductType type, Guid? categoryId, int priceInFen, int durationDays)
    {
        if (type == VipProductType.Category && !categoryId.HasValue)
        {
            throw new ArgumentException("CategoryId is required when VIP product type is Category.");
        }

        var product = new VipProduct
        {
            Name = name,
            Type = type,
            CategoryId = type == VipProductType.Category ? categoryId : null,
            PriceInFen = priceInFen,
            DurationDays = durationDays,
            IsEnabled = true
        };

        dbContext.VipProducts.Add(product);
        await dbContext.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Guid id, string name, VipProductType type, Guid? categoryId, int priceInFen, int durationDays, bool isEnabled)
    {
        if (type == VipProductType.Category && !categoryId.HasValue)
        {
            throw new ArgumentException("CategoryId is required when VIP product type is Category.");
        }

        var product = await dbContext.VipProducts.FindAsync(id);
        if (product == null) return;

        product.Name = name;
        product.Type = type;
        product.CategoryId = type == VipProductType.Category ? categoryId : null;
        product.PriceInFen = priceInFen;
        product.DurationDays = durationDays;
        product.IsEnabled = isEnabled;

        await dbContext.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await dbContext.VipProducts.FindAsync(id);
        if (product == null) return;

        dbContext.VipProducts.Remove(product);
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<VipProduct>> GetEnabledAsync(Guid? categoryId = null, VipProductType? type = null)
    {
        var query = dbContext.VipProducts
            .Include(v => v.Category)
            .Where(v => v.IsEnabled);

        if (type.HasValue)
        {
            query = query.Where(v => v.Type == type.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(v => v.CategoryId == categoryId.Value);
        }

        return await query
            .OrderBy(v => v.Name)
            .ToListAsync();
    }
}
