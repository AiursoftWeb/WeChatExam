using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Models.VipMembersViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create VIP Membership";
    }

    [Required]
    [Display(Name = "User ID")]
    public string UserId { get; set; } = string.Empty;

    public string? UserName { get; set; }

    [Required]
    [Display(Name = "VIP Product")]
    public Guid VipProductId { get; set; }

    public List<SelectListItem> VipProducts { get; set; } = [];

    [Required]
    [Display(Name = "Start Time")]
    [DataType(DataType.DateTime)]
    public DateTime StartTime { get; set; } = DateTime.UtcNow;

    [Required]
    [Display(Name = "End Time")]
    [DataType(DataType.DateTime)]
    public DateTime EndTime { get; set; } = DateTime.UtcNow.AddYears(1);
}
