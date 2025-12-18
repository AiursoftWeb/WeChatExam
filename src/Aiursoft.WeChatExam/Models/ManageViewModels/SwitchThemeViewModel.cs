using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.ManageViewModels;

public class SwitchThemeViewModel
{
    [Required]
    public required string Theme { get; set; }
}
