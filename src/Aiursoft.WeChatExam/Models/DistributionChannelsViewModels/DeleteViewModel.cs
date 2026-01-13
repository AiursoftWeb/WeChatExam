using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete Distribution Channel";
    }

    public required DistributionChannel Channel { get; set; }
    public int RegistrationCount { get; set; }
}
