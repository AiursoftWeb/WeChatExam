using Aiursoft.UiStack.Layout;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Models.VipMembersViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "VIP Members";
    }

    public List<VipMembership> VipMembers { get; set; } = [];
    public string? SearchQuery { get; set; }
    public string? UserId { get; set; }
    public User? TargetUser { get; set; }

    public int TotalCount { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
