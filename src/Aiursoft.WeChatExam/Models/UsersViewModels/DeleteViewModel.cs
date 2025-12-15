using Aiursoft.WeChatExam.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.UsersViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete User";
    }

    public required User User { get; set; }
}
