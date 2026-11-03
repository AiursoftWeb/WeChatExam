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
            .Include(v => v.Institution)
            .OrderByDescending(v => v.CreationTime)
            .ToListAsync();
    }

    public async Task<VipProduct?> GetByIdAsync(Guid id)
    {
        return await dbContext.VipProducts
            .Include(v => v.TotalCategory)
            .Include(v => v.Institution)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    public async Task<VipProduct> CreateAsync(string name, VipProductType type, Guid? totalCategoryId, int priceInFen, int durationDays, Guid? institutionId = null)
    {
        await ValidateTargetAsync(type, totalCategoryId, institutionId);

        var product = new VipProduct
        {
            Name = name,
            Type = type,
            TotalCategoryId = type == VipProductType.Category ? totalCategoryId : null,
            InstitutionId = type == VipProductType.Institution ? institutionId : null,
            PriceInFen = priceInFen,
            DurationDays = durationDays,
            IsEnabled = true
        };

        dbContext.VipProducts.Add(product);
        await dbContext.SaveChangesAsync();
        return product;
    }

    public async Task UpdateAsync(Guid id, string name, VipProductType type, Guid? totalCategoryId, int priceInFen, int durationDays, bool isEnabled, Guid? institutionId = null)
    {
        await ValidateTargetAsync(type, totalCategoryId, institutionId);

        var product = await dbContext.VipProducts.FindAsync(id);
        if (product == null) return;

        var targetCourse = type == VipProductType.Category ? totalCategoryId : null;
        var targetInstitution = type == VipProductType.Institution ? institutionId : null;
        // Memberships and pending orders refer to this product. Retargeting it would
        // silently change what a student has bought, including legacy all-school VIPs.
        if ((product.Type != type || product.TotalCategoryId != targetCourse || product.InstitutionId != targetInstitution)
            && await HasPurchasesAsync(id))
        {
            throw new InvalidOperationException("This product has orders or memberships. Create a new product to change its access scope.");
        }

        product.Name = name;
        product.Type = type;
        product.TotalCategoryId = targetCourse;
        product.InstitutionId = targetInstitution;
        product.PriceInFen = priceInFen;
        product.DurationDays = durationDays;
        product.IsEnabled = isEnabled;

        await dbContext.SaveChangesAsync();
    }

    public async Task<bool> HasPurchasesAsync(Guid id) =>
        await dbContext.PaymentOrders.AnyAsync(o => o.VipProductId == id) ||
        await dbContext.VipMemberships.AnyAsync(m => m.VipProductId == id);

    private async Task ValidateTargetAsync(VipProductType type, Guid? totalCategoryId, Guid? institutionId)
    {
        // Type 1 remains reserved for historical records, but cannot be issued again.
        if (type is not (VipProductType.Category or VipProductType.Institution))
            throw new ArgumentException("Invalid VIP product type.");
        if (type == VipProductType.Category &&
            (!totalCategoryId.HasValue || !await dbContext.TotalCategories.AnyAsync(c => c.Id == totalCategoryId.Value)))
            throw new ArgumentException("A course VIP requires an existing total category.");
        if (type == VipProductType.Institution &&
            (!institutionId.HasValue || !await dbContext.Institutions.AnyAsync(i => i.Id == institutionId.Value)))
            throw new ArgumentException("An institution VIP requires an existing institution.");
    }

    public async Task DeleteAsync(Guid id)
    {
        var product = await dbContext.VipProducts.FindAsync(id);
        if (product == null) return;

        if (await HasPurchasesAsync(id))
            throw new InvalidOperationException("This product has orders or memberships. Disable it instead of deleting it.");

        var changes = await dbContext.Changes.Where(c => c.VipProductId == id).ToListAsync();
        if (changes.Count > 0)
        {
            if (product.Type != VipProductType.RealExam || product.IsEnabled)
                throw new InvalidOperationException("This product has audit history. Disable it instead of deleting it.");

            foreach (var change in changes)
            {
                change.ArchivedVipProductId = product.Id;
                change.ArchivedVipProductName = product.Name;
                change.VipProductId = null;
            }
        }

        dbContext.VipProducts.Remove(product);
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<VipProduct>> GetEnabledAsync(Guid? totalCategoryId = null, VipProductType? type = null, Guid? institutionId = null)
    {
        var query = dbContext.VipProducts
            .Include(v => v.TotalCategory)
            .Include(v => v.Institution)
            .Where(v => v.IsEnabled && (v.Type == VipProductType.Category || v.Type == VipProductType.Institution));

        if (type.HasValue)
        {
            query = query.Where(v => v.Type == type.Value);
        }

        if (totalCategoryId.HasValue)
        {
            query = query.Where(v => v.TotalCategoryId == totalCategoryId.Value);
        }

        if (institutionId.HasValue)
        {
            query = query.Where(v => v.Type == VipProductType.Institution && v.InstitutionId == institutionId.Value);
        }

        return await query
            .OrderBy(v => v.Name)
            .ToListAsync();
    }
}
