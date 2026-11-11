using System.Data.Common;
using System.Security.Claims;
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
    WeChatExamDbContext dbContext,
    VirtualPaymentRefundReconciliationService refunds,
    ILogger<PaymentOrdersController> logger) : Controller
{
    // GET: PaymentOrders
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 70,
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

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> VirtualPaymentReadiness([FromServices] VirtualPaymentReadinessService readiness) =>
        this.StackView(await readiness.CheckAsync(HttpContext.RequestAborted));

    // GET: PaymentOrders/RefundReviews
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    public async Task<IActionResult> RefundReviews(Guid? orderId, int page = 1)
    {
        const int pageSize = 20;
        if (!ModelState.IsValid || page < 1 || page > int.MaxValue / pageSize) return BadRequest();
        // Audit records are not a mutable task queue. A manual membership edit
        // must never erase refund evidence or silently mark a review resolved.
        var query = dbContext.PaymentLogs.AsNoTracking().Where(l => l.EventType == "VirtualPaymentRefundReview" &&
            l.PaymentOrder != null && l.PaymentOrder.Channel == PaymentChannel.WeChatVirtual);
        if (orderId.HasValue) query = query.Where(l => l.PaymentOrderId == orderId.Value);
        var totalCount = await query.CountAsync();
        var logs = await query.Include(l => l.PaymentOrder).ThenInclude(o => o!.User)
            .Include(l => l.PaymentOrder).ThenInclude(o => o!.VipProduct)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var orderIds = logs.Select(l => l.PaymentOrderId).Distinct().ToList();
        var resolutions = await dbContext.PaymentLogs.AsNoTracking().Where(l => orderIds.Contains(l.PaymentOrderId) &&
            l.EventType == "VirtualPaymentRefunded").ToListAsync();
        return this.StackView(new RefundReviewsViewModel
        {
            Reviews = logs.Select(l => RefundReviewItem.FromLog(l, resolutions.Where(r => r.PaymentOrderId == l.PaymentOrderId).ToList())).ToList(), TotalCount = totalCount,
            CurrentPage = page, PageSize = pageSize, OrderIdFilter = orderId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> ReconcileRefund(Guid id)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        VirtualPaymentRefundReconciliationResult result;
        try { result = await refunds.ReconcileAsync(id, actor, HttpContext.RequestAborted); }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            logger.LogError("Refund reconciliation persistence failed for order {OrderId}.", id);
            result = VirtualPaymentRefundReconciliationResult.Unavailable;
        }
        if (result == VirtualPaymentRefundReconciliationResult.NotFound) return NotFound();
        TempData["RefundReconciliationResult"] = (int)result;
        return RedirectToAction(nameof(RefundReviews), new { orderId = id });
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
            .ThenInclude(p => p!.TotalCategory)
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
