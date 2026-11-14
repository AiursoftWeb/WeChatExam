using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// 支付订单管理服务实现
/// </summary>
public class PaymentOrderService(WeChatExamDbContext dbContext) : IPaymentOrderService
{
    public async Task<List<PaymentOrder>> GetAllOrdersAsync(int page = 1, int pageSize = 50, PaymentOrderStatus? statusFilter = null, string? userIdFilter = null, string? orderNumberFilter = null)
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
        if (!string.IsNullOrWhiteSpace(orderNumberFilter))
        {
            var orderNumber = orderNumberFilter.Trim();
            query = query.Where(o => o.OutTradeNo == orderNumber);
        }

        // Load names separately so historical orders with missing user rows remain visible.
        var orders = await query
            .Include(o => o.VirtualPaymentSnapshot)
            .Include(o => o.RefundRequest)
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.TotalCategory)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        var userIds = orders.Select(o => o.UserId).Distinct().ToList();
        var users = await dbContext.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        foreach (var order in orders) order.User = users.GetValueOrDefault(order.UserId);
        return orders;
    }

    public async Task<PaymentOrder?> GetOrderDetailAsync(Guid id)
    {
        return await dbContext.PaymentOrders
            .Include(o => o.VirtualPaymentSnapshot)
            .Include(o => o.RefundRequest)
            .Include(o => o.PaymentLogs)
            .Include(o => o.Coupon)
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.TotalCategory)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<List<PaymentOrder>> GetUserPaymentsAsync(string userId)
    {
        return await dbContext.PaymentOrders
            .Include(o => o.VirtualPaymentSnapshot)
            .Include(o => o.RefundRequest)
            .Include(o => o.User)
            .Include(o => o.VipProduct)
            .ThenInclude(p => p!.TotalCategory)
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<int> GetOrderCountAsync(PaymentOrderStatus? statusFilter = null, string? userIdFilter = null, string? orderNumberFilter = null)
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
        if (!string.IsNullOrWhiteSpace(orderNumberFilter))
        {
            var orderNumber = orderNumberFilter.Trim();
            query = query.Where(o => o.OutTradeNo == orderNumber);
        }
        
        return await query.CountAsync();
    }
}
