using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.WeChatExam.Models.UsersViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "User Details";
    }

    public required User User { get; set; }

    public required IList<IdentityRole> Roles { get; set; }

    public required List<PermissionDescriptor> Permissions { get; set; }
}
