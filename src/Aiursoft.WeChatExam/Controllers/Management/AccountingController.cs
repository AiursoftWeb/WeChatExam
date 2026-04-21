using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.AccountingViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class AccountingController(
    ChangeService changeService,
    WeChatExamDbContext dbContext) : Controller
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
        
        // General Stats
        var totalUsers = await dbContext.Users.CountAsync();
        var now = DateTime.UtcNow;
        var activePaidUsers = await dbContext.VipMemberships
            .Where(v => v.StartTime <= now && v.EndTime > now)
            .Select(v => v.UserId)
            .Distinct()
            .CountAsync();

        var day30DaysAgo = now.AddDays(-30);
        var newUsersLast30Days = await dbContext.Users
            .Where(u => u.CreationTime >= day30DaysAgo)
            .CountAsync();
        
        var newVipActivationsLast30Days = await dbContext.Changes
            .Where(c => c.CreateTime >= day30DaysAgo && 
                       (c.Type == Entities.ChangeType.VipActivatedViaPayment || 
                        c.Type == Entities.ChangeType.VipActivatedViaCoupon || 
                        c.Type == Entities.ChangeType.VipActivatedViaAdmin))
            .CountAsync();

        var model = new IndexViewModel
        {
            Reports = reports,
            TotalUsers = totalUsers,
            ActivePaidUsers = activePaidUsers,
            NewUsersLast30Days = newUsersLast30Days,
            NewVipActivationsLast30Days = newVipActivationsLast30Days
        };

        // Chart Data
        var usersHistory = await dbContext.Users
            .Where(t => t.CreationTime > day30DaysAgo)
            .GroupBy(t => t.CreationTime.Date)
            .Select(t => new { Time = t.Key, Count = t.Count() })
            .ToListAsync();

        var vipHistory = await dbContext.Changes
            .Where(t => t.CreateTime > day30DaysAgo && 
                       (t.Type == Entities.ChangeType.VipActivatedViaPayment || 
                        t.Type == Entities.ChangeType.VipActivatedViaCoupon || 
                        t.Type == Entities.ChangeType.VipActivatedViaAdmin))
            .GroupBy(t => t.CreateTime.Date)
            .Select(t => new { Time = t.Key, Count = t.Count() })
            .ToListAsync();

        for (var i = 0; i <= 30; i++)
        {
            var date = day30DaysAgo.Date.AddDays(i);
            model.ChartLabels.Add(date.ToString("MM-dd"));
            model.NewUsersData.Add(usersHistory.FirstOrDefault(t => t.Time == date)?.Count ?? 0);
            model.NewVipData.Add(vipHistory.FirstOrDefault(t => t.Time == date)?.Count ?? 0);
        }

        return this.StackView(model);
    }

    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 3,
        LinkText = "VIP Change History",
        LinkOrder = 6)]
    public async Task<IActionResult> History()
    {
        var changes = await changeService.GetAllHistory();
        return this.StackView(new HistoryViewModel
        {
            Changes = changes
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
