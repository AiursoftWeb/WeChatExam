using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create Distribution Channel";
    }

    [Required(ErrorMessage = "Agency name is required")]
    [MaxLength(200, ErrorMessage = "The {0} cannot exceed {1} characters.")]
    [Display(Name = "Agency Name")]
    public string AgencyName { get; set; } = string.Empty;
}
