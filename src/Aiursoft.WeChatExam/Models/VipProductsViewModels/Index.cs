using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "VIP Products";
    }

    public List<VipProduct> Products { get; set; } = [];
    public string? SearchQuery { get; set; }
    public bool ShowDisabled { get; set; }
    public int EnabledCount { get; set; }
    public int DisabledCount { get; set; }
    public Guid? TotalCategoryId { get; set; }
    public string? ScopeTitle { get; set; }
    public Guid? InstitutionId { get; set; }
    public int PayingBuyerCount { get; init; }
    public bool CanReadSales { get; init; }
    public Dictionary<Guid, ProductSalesSummary> Sales { get; init; } = [];
}
