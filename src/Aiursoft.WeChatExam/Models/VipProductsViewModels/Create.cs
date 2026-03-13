using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create VIP Product";
    }

    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public int PriceInFen { get; set; }
    public int DurationDays { get; set; } = 365;
    public List<SelectListItem> Categories { get; set; } = [];
}
