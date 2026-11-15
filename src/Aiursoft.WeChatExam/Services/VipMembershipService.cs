using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class VipMembershipService(WeChatExamDbContext dbContext) : IVipMembershipService
{
    public async Task<List<VipMembership>> GetVipStatusListAsync(string userId)
    {
        return await dbContext.VipMemberships.AsNoTracking()
            .Include(v => v.User)
            .Include(v => v.VipProduct)
            .ThenInclude(p => p!.TotalCategory)
            .Include(v => v.VipProduct)
            .ThenInclude(p => p!.Institution)
            .Where(v => v.UserId == userId)
            .ToListAsync();
    }

}
