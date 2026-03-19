using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit VIP Product";
    }

    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public VipProductType Type { get; set; }
    public Guid? CategoryId { get; set; }
    public int PriceInFen { get; set; }
    public int DurationDays { get; set; }
    public bool IsEnabled { get; set; }
    public List<SelectListItem> Categories { get; set; } = [];
}
