using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class ActiveUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class ActiveUserDetail
{
    public required ActiveUserInfo User { get; set; }
    public required Change ActivationEvent { get; set; }
}

public class MonthlyActiveUserReport
{
    public DateTime Month { get; set; }
    public List<ActiveUserDetail> ActiveUsers { get; set; } = [];
}

public class ChangeService(WeChatExamDbContext dbContext) : IScopedDependency
{
    public async Task RecordChange(
        ChangeType type, 
        string targetUserId, 
        string? triggerUserId = null, 
        Guid? vipProductId = null, 
        Guid? couponId = null, 
        string details = "")
    {
        var change = new Change
        {
            Type = type,
            TargetUserId = targetUserId,
            TriggerUserId = triggerUserId,
            VipProductId = vipProductId,
            CouponId = couponId,
            Details = details,
            CreateTime = DateTime.UtcNow
        };
        dbContext.Changes.Add(change);
        await dbContext.SaveChangesAsync();
    }

    public async Task<List<MonthlyActiveUserReport>> GetMonthlyReports(int months = 24)
    {
        var now = DateTime.UtcNow;
        var reports = new List<MonthlyActiveUserReport>();

        for (var i = 0; i < months; i++)
        {
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-i);
            var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
            
            var report = await GetReportForMonth(monthStart, monthEnd);
            reports.Add(report);
        }

        return reports;
    }

    public async Task<MonthlyActiveUserReport> GetReportForMonth(DateTime start, DateTime end)
    {
        // 查找在该月份内发生的激活事件
        var changesInMonth = await dbContext.Changes
            .Include(c => c.TargetUser)
            .Include(c => c.VipProduct)
            .Include(c => c.Coupon)
            .Where(c => c.CreateTime >= start && c.CreateTime <= end)
            .OrderByDescending(c => c.CreateTime)
            .ToListAsync();

        var reportDetails = changesInMonth.Select(c => new ActiveUserDetail
        {
            User = new ActiveUserInfo
            {
                Id = c.TargetUserId,
                UserName = c.TargetUser?.UserName ?? "Deleted",
                DisplayName = c.TargetUser?.DisplayName ?? "Deleted User"
            },
            ActivationEvent = c
        }).ToList();

        return new MonthlyActiveUserReport
        {
            Month = start,
            ActiveUsers = reportDetails
        };
    }
}
