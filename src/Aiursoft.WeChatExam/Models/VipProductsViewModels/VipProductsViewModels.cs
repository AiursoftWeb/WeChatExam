using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public List<VipProduct> Products { get; set; } = [];
    public string? SearchQuery { get; set; }
}

public class CreateViewModel : UiStackLayoutViewModel
{
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public int PriceInFen { get; set; }
    public int DurationDays { get; set; } = 365;
    public List<SelectListItem> Categories { get; set; } = [];
}

public class EditViewModel : UiStackLayoutViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public int PriceInFen { get; set; }
    public int DurationDays { get; set; }
    public bool IsEnabled { get; set; }
    public List<SelectListItem> Categories { get; set; } = [];
}

public class DeleteViewModel : UiStackLayoutViewModel
{
    public required VipProduct Product { get; set; }
}
