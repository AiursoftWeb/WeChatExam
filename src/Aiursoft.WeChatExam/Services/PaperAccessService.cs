using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public class PaperAccessService : IPaperAccessService
{
    private readonly IWeChatPayService _payService;

    public PaperAccessService(IWeChatPayService payService)
    {
        _payService = payService;
    }

    public async Task<PaperAccessStatus> GetUserAccessStatusAsync(string? userId)
    {
        var status = new PaperAccessStatus();
        
        if (userId != null)
        {
            var vips = await _payService.GetVipStatusListAsync(userId);
            var activeVips = vips.Where(v => v.IsActive && v.VipProduct != null).ToList();
            
            status.ActiveCategoryVips = activeVips
                .Where(v => v.VipProduct!.Type == VipProductType.Category && v.VipProduct.CategoryId.HasValue)
                .Select(v => v.VipProduct!.CategoryId!.Value)
                .ToHashSet();
                
            status.HasRealExamVip = activeVips.Any(v => v.VipProduct!.Type == VipProductType.RealExam);
        }

        return status;
    }

    public bool HasAccess(Paper paper, PaperAccessStatus accessStatus)
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
