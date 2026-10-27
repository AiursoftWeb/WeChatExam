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
            .Include(v => v.TotalCategory)
            .OrderByDescending(v => v.CreationTime)
            .ToListAsync();
    }

    public async Task<VipProduct?> GetByIdAsync(Guid id)
    {
        return await dbContext.VipProducts
            .Include(v => v.TotalCategory)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<VipProduct> CreateAsync(string name, VipProductType type, Guid? totalCategoryId, int priceInFen, int durationDays)
    {
        if (!Enum.IsDefined(type) || (type == VipProductType.Category &&
            (!totalCategoryId.HasValue || !await dbContext.TotalCategories.AnyAsync(c => c.Id == totalCategoryId.Value))))
        {
            throw new ArgumentException("A course VIP requires an existing total category.");
        }

        var product = new VipProduct
        {
            Name = name,
            Type = type,
            TotalCategoryId = type == VipProductType.Category ? totalCategoryId : null,
            PriceInFen = priceInFen,
            DurationDays = durationDays,
            IsEnabled = true
        };

        dbContext.VipProducts.Add(product);
        await dbContext.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Guid id, string name, VipProductType type, Guid? totalCategoryId, int priceInFen, int durationDays, bool isEnabled)
    {
        if (!Enum.IsDefined(type) || (type == VipProductType.Category &&
            (!totalCategoryId.HasValue || !await dbContext.TotalCategories.AnyAsync(c => c.Id == totalCategoryId.Value))))
        {
            throw new ArgumentException("A course VIP requires an existing total category.");
        }

        var product = await dbContext.VipProducts.FindAsync(id);
        if (product == null) return;

        product.Name = name;
        product.Type = type;
        product.TotalCategoryId = type == VipProductType.Category ? totalCategoryId : null;
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

    public async Task<List<VipProduct>> GetEnabledAsync(Guid? totalCategoryId = null, VipProductType? type = null)
    {
        var query = dbContext.VipProducts
            .Include(v => v.TotalCategory)
            .Where(v => v.IsEnabled);

        if (type.HasValue)
        {
            query = query.Where(v => v.Type == type.Value);
        }

        if (totalCategoryId.HasValue)
        {
            query = query.Where(v => v.TotalCategoryId == totalCategoryId.Value);
        }

        return await query
            .OrderBy(v => v.Name)
            .ToListAsync();
    }
}
