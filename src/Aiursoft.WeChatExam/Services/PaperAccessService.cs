using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public class PaperAccessService : IPaperAccessService
{
    private readonly IWeChatPayService _payService;
    private PaperAccessStatus? _cachedStatus;
    private string? _cachedUserId;

    public PaperAccessService(IWeChatPayService payService)
    {
        _payService = payService;
    }

    public async Task<PaperAccessStatus> GetUserAccessStatusAsync(string? userId)
    {
        if (_cachedStatus != null && _cachedUserId == userId)
        {
            return _cachedStatus;
        }

        var status = new PaperAccessStatus();
        
        if (userId != null)
        {
            var vips = await _payService.GetVipStatusListAsync(userId);
            var activeVips = vips.Where(v => v.IsActive && v.VipProduct != null).ToList();
            
            // Existing course IDs were retained when VIP products moved to TotalCategories.
            // The remaining legacy content readers consume those same IDs until their migration.
            status.ActiveCategoryVips = activeVips
                .Where(v => v.VipProduct!.Type == VipProductType.Category && v.VipProduct.TotalCategoryId.HasValue)
                .Select(v => v.VipProduct!.TotalCategoryId!.Value)
                .ToHashSet();
                
            status.HasRealExamVip = activeVips.Any(v => v.VipProduct!.Type == VipProductType.RealExam);
        }

        _cachedStatus = status;
        _cachedUserId = userId;

        return status;
    }

    public bool HasAccess(Paper paper, PaperAccessStatus accessStatus) => CanAccess(paper, accessStatus);

    // The content checker uses the same decision without resolving payment credentials
    // or loading any customer's memberships.
    public static bool CanAccess(Paper paper, PaperAccessStatus accessStatus)
    {
        if (paper.IsFree)
        {
            return true;
        }
        
        if (paper.IsRealExam)
        {
            return accessStatus.HasRealExamVip;
        }
        
        var categoryIds = paper.PaperCategories.Select(pc => pc.CategoryId).ToList();
        return categoryIds.Any(catId => accessStatus.ActiveCategoryVips.Contains(catId));
    }
}
