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

        var memberships = userId == null ? [] : await _payService.GetVipStatusListAsync(userId);
        var status = FromMemberships(memberships);

        _cachedStatus = status;
        _cachedUserId = userId;

        return status;
    }

    // Membership validity and entitlement targets are identical for payment-backed API
    // readers and scheduled exams reading persisted memberships without payment credentials.
    public static PaperAccessStatus FromMemberships(IEnumerable<VipMembership> memberships)
    {
        var active = memberships.Where(m => m.IsActive && m.VipProduct != null)
            .Select(m => m.VipProduct!).ToList();
        return new PaperAccessStatus
        {
            ActiveCategoryVips = active.Where(p => p.Type == VipProductType.Category && p.TotalCategoryId.HasValue)
                .Select(p => p.TotalCategoryId!.Value).ToHashSet(),
            ActiveInstitutionVips = active.Where(p => p.Type == VipProductType.Institution && p.InstitutionId.HasValue)
                .Select(p => p.InstitutionId!.Value).ToHashSet(),
            HasRealExamVip = active.Any(p => p.Type == VipProductType.RealExam)
        };
    }

    // Whole-paper entitlement is shared by practice and admission to scheduled exams.
    // It never grants independent question-bank rights.
    public static bool CanAccess(MockPaper paper, PaperAccessStatus access) =>
        paper.AllowViewAnonymousely || access.ActiveCategoryVips.Contains(paper.TotalCategoryId);

    public static bool CanAccess(RealPaper paper, PaperAccessStatus access) =>
        CanAccessInstitution(paper.InstitutionId, access);

    public static bool CanAccessInstitution(Guid institutionId, PaperAccessStatus access) =>
        access.HasRealExamVip || access.ActiveInstitutionVips.Contains(institutionId);

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
