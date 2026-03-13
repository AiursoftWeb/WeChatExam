using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 支付订单管理服务实现
/// </summary>
public class PaymentOrderService(WeChatExamDbContext dbContext) : IPaymentOrderService
{
    public async Task<List<PaymentOrder>> GetAllOrdersAsync(int page = 1, int pageSize = 50, PaymentOrderStatus? statusFilter = null, string? userIdFilter = null)
    {
        var query = dbContext.PaymentOrders.AsQueryable();

        if (statusFilter.HasValue)
        {
            query = query.Where(o => o.Status == statusFilter.Value);
        }

        if (!string.IsNullOrEmpty(userIdFilter))
        {
            query = query.Where(o => o.UserId == userIdFilter);
        }

        // Including VipProduct and Category to ensure display name is available
        return await query
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.Category)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<PaymentOrder?> GetOrderDetailAsync(Guid id)
    {
        return await dbContext.PaymentOrders
            .Include(o => o.PaymentLogs)
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.Category)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<PaymentOrder>> GetUserPaymentsAsync(string userId)
    {
        return await dbContext.PaymentOrders
            .Include(o => o.User)
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.Category)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> GetOrderCountAsync(PaymentOrderStatus? statusFilter = null)
    {
        var query = dbContext.PaymentOrders.AsQueryable();
        if (statusFilter.HasValue)
        {
            query = query.Where(o => o.Status == statusFilter.Value);
        }
        return await query.CountAsync();
    }
}
