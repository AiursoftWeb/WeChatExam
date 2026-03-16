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
}
