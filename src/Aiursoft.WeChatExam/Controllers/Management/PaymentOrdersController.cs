using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class PaymentOrdersController(
    IPaymentOrderService paymentOrderService,
    WeChatExamDbContext dbContext) : Controller
{
    // GET: PaymentOrders
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 3,
        LinkText = "Payment Orders",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(PaymentOrderStatus? status, string? userId, int page = 1, int pageSize = 50)
    {
        var orders = await paymentOrderService.GetAllOrdersAsync(page, pageSize, status, userId);
        
        // Ensure VipProducts are loaded for orders implicitly or directly loaded in the service.
        var totalCount = await paymentOrderService.GetOrderCountAsync(status, userId);

        return this.StackView(new IndexViewModel
        {
            Orders = orders,
            StatusFilter = status,
            UserIdFilter = userId,
            TotalCount = totalCount,
            CurrentPage = page,
            PageSize = pageSize
        });
    }

    // GET: PaymentOrders/Details/{id}
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var order = await paymentOrderService.GetOrderDetailAsync(id.Value);
        if (order == null) return NotFound();

        return this.StackView(new DetailsViewModel { Order = order });
    }

    // GET: PaymentOrders/UserPayments/{userId}
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    public async Task<IActionResult> UserPayments(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return NotFound();

        var orders = await paymentOrderService.GetUserPaymentsAsync(userId);
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId);
        var vips = await dbContext.VipMemberships
            .Include(v => v.VipProduct)
            .ThenInclude(p => p!.Category)
            .Where(v => v.UserId == userId)
            .ToListAsync();

        return this.StackView(new UserPaymentsViewModel
        {
            UserId = userId,
            UserDisplayName = user?.DisplayName,
            Orders = orders,
            VipMemberships = vips
        });
    }
}
