using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.AccountingViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class AccountingController(ChangeService changeService) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)] // 借用现有权限
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 3,
        LinkText = "Finance Reports",
        LinkOrder = 5)]
    public async Task<IActionResult> Index()
    {
        var reports = await changeService.GetMonthlyReports();
        return this.StackView(new IndexViewModel
        {
            Reports = reports
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Details(DateTime month)
    {
        var start = new DateTime(month.Year, month.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1).AddTicks(-1);

        var report = await changeService.GetReportForMonth(start, end);
        var model = new DetailsViewModel
        {
            Report = report
        };
        model.PageTitle = $"Finance Report - {month:yyyy-MM}";
        
        return this.StackView(model);
    }
}
