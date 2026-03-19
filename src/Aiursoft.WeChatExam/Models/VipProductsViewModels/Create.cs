using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.VipProductsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create VIP Product";
    }

    [Required]
    public string Name { get; set; } = string.Empty;
    public VipProductType Type { get; set; } = VipProductType.Category;
    public Guid? CategoryId { get; set; }
    
    [Range(1, int.MaxValue)]
    public int PriceInFen { get; set; }
    
    [Range(1, int.MaxValue)]
    public int DurationDays { get; set; } = 365;
    public List<SelectListItem> Categories { get; set; } = [];
}
