using Aiursoft.UiStack.Navigation;
using Aiursoft.UiStack.Layout;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class JoinExamController : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Exam Management",
        CascadedLinksIcon = "award",
        CascadedLinksOrder = 2,
        LinkText = "Join Official Exam",
        LinkOrder = 2)]
    public IActionResult Index()
    {
        return this.StackView(new UiStackLayoutViewModel { PageTitle = "Join Official Exam" });
    }
}
