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

[Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
[LimitPerMin]
public class RefundsController(WeChatExamDbContext db, IPaymentOrderService orders,
    AdminVirtualRefundService refunds, ILogger<RefundsController> logger) : Controller
{
    [HttpGet]
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management", CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 70, LinkText = "Refund management", LinkOrder = 2)]
    public async Task<IActionResult> Index(int page = 1)
    {
        const int size = 20;
        if (page < 1 || page > int.MaxValue / size) return BadRequest();
        var query = db.PaymentOrders.AsNoTracking().Where(o => o.RefundRequest != null ||
            o.Status == PaymentOrderStatus.Refunded || o.PaymentLogs.Any(l => l.EventType == "VirtualPaymentRefundReview"));
        var count = await query.CountAsync();
        var result = await query.Include(o => o.VirtualPaymentSnapshot).Include(o => o.RefundRequest)
            .Include(o => o.VipProduct).Include(o => o.PaymentLogs)
            .OrderByDescending(o => o.RefundRequest != null ? o.RefundRequest.CreatedAt :
                o.PaymentLogs.Where(l => l.EventType == "VirtualPaymentRefunded" || l.EventType == "VirtualPaymentRefundReview")
                    .Select(l => (DateTime?)l.CreatedAt).Max() ?? o.CreatedAt)
            .ThenBy(o => o.Id).Skip((page - 1) * size).Take(size).ToListAsync();
        var ids = result.Select(o => o.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id);
        foreach (var order in result) order.User = users.GetValueOrDefault(order.UserId);
        return this.StackView(new RefundManagementViewModel { Orders = result, TotalCount = count, CurrentPage = page });
    }

    [HttpGet]
    public async Task<IActionResult> Process(Guid id)
    {
        var order = await orders.GetOrderDetailAsync(id);
        return order == null ? NotFound() : this.StackView(new RefundOrderViewModel { Order = order });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanRefundPaymentOrders)]
    public Task<IActionResult> Verify(Guid id) => Operate(id, actor => refunds.VerifyPlatformAsync(id, actor, HttpContext.RequestAborted));

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanRefundPaymentOrders)]
    public Task<IActionResult> Submit(Guid id, int amountInFen, bool confirmed)
    {
        if (!confirmed || amountInFen <= 0) return Task.FromResult<IActionResult>(BadRequest());
        return Operate(id, actor => refunds.RequestAsync(id, amountInFen, actor, HttpContext.RequestAborted));
    }

    [HttpPost, ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanRefundPaymentOrders)]
    public Task<IActionResult> Check(Guid id) => Operate(id, actor => refunds.CheckAsync(id, actor, HttpContext.RequestAborted));

    private async Task<IActionResult> Operate(Guid id, Func<string, Task<AdminRefundResult>> action)
    {
        if (!ModelState.IsValid || id == Guid.Empty) return BadRequest();
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actor)) return Forbid();
        AdminRefundResult result;
        try { result = await action(actor); }
        catch (Exception ex) when (ex is DbUpdateException or DbException or InvalidOperationException)
        {
            logger.LogError("Staff refund operation could not complete for order {OrderId}; query the existing intent.", id);
            result = AdminRefundResult.Unavailable;
        }
        TempData["AdminRefundResult"] = (int)result;
        return RedirectToAction(nameof(Process), new { id });
    }
}
