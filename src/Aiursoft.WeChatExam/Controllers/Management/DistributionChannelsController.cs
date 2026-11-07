using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using MySqlConnector;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class DistributionChannelsController(
    IDistributionChannelService distributionChannelService,
    ICouponService couponService,
    IVipProductService vipProductService,
    WeChatExamDbContext dbContext,
    IAuthorizationService authorizationService,
    IStringLocalizer<DistributionChannelsController> localizer) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanReadDistributionChannels)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Marketing Management",
        CascadedLinksIcon = "megaphone",
        CascadedLinksOrder = 80,
        LinkText = "Distribution Channels",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? search)
    {
        var channels = await distributionChannelService.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            channels = channels
                .Where(c => c.AgencyName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            c.Coupons.Any(co => co.Code.Contains(search, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var channelStats = new Dictionary<Guid, ChannelStats>();
        foreach (var channel in channels)
        {
            channelStats[channel.Id] = await distributionChannelService.GetStatsAsync(channel.Id);
        }

        return this.StackView(new IndexViewModel
        {
            Channels = channels,
            ChannelStats = channelStats,
            SearchQuery = search
        });
    }

    [Authorize(Policy = AppPermissionNames.CanAddDistributionChannels)]
    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAddDistributionChannels)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        await distributionChannelService.CreateAsync(model.AgencyName);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanReadDistributionChannels)]
    public async Task<IActionResult> Details(Guid? id, string? returnUrl = null)
    {
        if (id == null) return NotFound();

        var channel = await distributionChannelService.GetByIdAsync(id.Value);
        if (channel == null) return NotFound();

        var stats = await distributionChannelService.GetStatsAsync(id.Value);

        var couponCounts = new Dictionary<Guid, int>();
        if ((await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanReadPaymentOrders)).Succeeded)
        {
            var couponIds = channel.Coupons.Select(c => c.Id).ToArray();
            couponCounts = await dbContext.PaymentOrders.AsNoTracking()
                .Where(o => o.Status == PaymentOrderStatus.Paid && o.CouponId.HasValue && couponIds.Contains(o.CouponId.Value))
                .GroupBy(o => o.CouponId!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Id, g => g.Count);
        }

        return this.StackView(new DetailsViewModel
        {
            Channel = channel,
            Stats = stats,
            CouponPaidOrderCounts = couponCounts,
            ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null
        });
    }

    [Authorize(Policy = AppPermissionNames.CanReadDistributionChannels)]
    [Authorize(Policy = AppPermissionNames.CanReadPaymentOrders)]
    public async Task<IActionResult> CouponDetails(Guid id, DateOnly? from, DateOnly? to, string? search, int page = 1, string timeZone = "Asia/Shanghai")
    {
        var coupon = await dbContext.Coupons.AsNoTracking()
            .Include(c => c.DistributionChannel)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (coupon == null) return NotFound();

        if (from > to)
            ModelState.AddModelError(nameof(from), localizer["The start date must not be after the end date."]);
        if (from < new DateOnly(1900, 1, 1) || to < new DateOnly(1900, 1, 1))
            ModelState.AddModelError(nameof(from), localizer["Choose dates on or after January 1, 1900."]);

        DateTime? startUtc = null;
        DateTime? endUtc = null;
        if (ModelState.IsValid && (from.HasValue || to.HasValue))
        {
            try
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
                if (from.HasValue) startUtc = TimeZoneInfo.ConvertTimeToUtc(from.Value.ToDateTime(TimeOnly.MinValue), zone);
                if (to.HasValue) endUtc = TimeZoneInfo.ConvertTimeToUtc(to.Value.ToDateTime(TimeOnly.MinValue).AddDays(1), zone);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                ModelState.AddModelError(nameof(from), localizer["The date range or time zone is not valid."]);
            }
        }

        // The order's actual coupon and settlement values are authoritative. Claims,
        // channel attribution and today's product/coupon prices are not sales history.
        var orders = dbContext.PaymentOrders.AsNoTracking()
            .Where(o => o.CouponId == id && o.Status == PaymentOrderStatus.Paid);
        if (!ModelState.IsValid)
        {
            orders = orders.Where(_ => false);
        }
        else
        {
            // Match the browser's calendar days, including historical daylight saving.
            if (startUtc.HasValue)
            {
                orders = orders.Where(o => o.PaidAt >= startUtc);
            }
            if (endUtc.HasValue)
            {
                orders = orders.Where(o => o.PaidAt < endUtc);
            }
        }
        search = search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            orders = orders.Where(o => (o.User != null && o.User.DisplayName.Contains(search)) ||
                                       o.Description.Contains(search) || o.OutTradeNo.Contains(search));
        }

        var count = await orders.CountAsync();
        var discount = await orders.SumAsync(o => (long?)o.DiscountInFen) ?? 0;
        var paid = await orders.SumAsync(o => (long?)o.AmountInFen) ?? 0;
        const int pageSize = 25;
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(count / (double)pageSize)));
        return this.StackView(new CouponDetailsViewModel
        {
            Coupon = coupon,
            Orders = await orders.Include(o => o.User)
                .OrderByDescending(o => o.PaidAt).ThenByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(),
            TotalCount = count,
            TotalDiscountInFen = discount,
            TotalPaidInFen = paid,
            From = from,
            To = to,
            Search = search,
            CurrentPage = page,
            TimeZone = timeZone
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> ToggleEnabled(Guid id, string? returnUrl = null)
    {
        var channel = await distributionChannelService.GetByIdAsync(id);
        if (channel == null) return NotFound();

        await distributionChannelService.SetEnabledAsync(id, !channel.IsEnabled);
        if (Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl!);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanDeleteDistributionChannels)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var channel = await distributionChannelService.GetByIdAsync(id.Value);
        if (channel == null) return NotFound();

        var stats = await distributionChannelService.GetStatsAsync(id.Value);

        return this.StackView(new DeleteViewModel
        {
            Channel = channel,
            Deletion = await distributionChannelService.GetDeletionSummaryAsync(id.Value),
            RegistrationCount = stats.RegistrationCount
        }, nameof(Delete));
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteDistributionChannels)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        try
        {
            await distributionChannelService.DeleteAsync(id);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex) when (IsDeletionConflict(ex))
        {
            dbContext.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, localizer["This channel has coupon history. Keep it for reference and disable the channel to stop future use."]);
            return await Delete(id);
        }
    }

    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> CreateCoupon(Guid channelId)
    {
        var channel = await distributionChannelService.GetByIdAsync(channelId);
        if (channel == null) return NotFound();

        var products = await vipProductService.GetAllAsync();

        return this.StackView(new CreateCouponViewModel
        {
            ChannelId = channelId,
            ChannelName = channel.AgencyName,
            AvailableVipProducts = products
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> CreateCoupon(CreateCouponViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableVipProducts = await vipProductService.GetAllAsync();
            return this.StackView(model);
        }

        try
        {
            await couponService.CreateAsync(
                model.ChannelId,
                model.Code,
                model.AmountInFen,
                model.IsSingleUse,
                model.TargetVipProductIds);

            return RedirectToAction(nameof(Details), new { id = model.ChannelId });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(nameof(model.Code), ex.Message);
            model.AvailableVipProducts = await vipProductService.GetAllAsync();
            return this.StackView(model);
        }
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> EditCouponAmount(Guid id, Guid channelId)
    {
        var coupon = await couponService.GetByIdAsync(id);
        if (coupon == null || coupon.DistributionChannelId != channelId) return NotFound();
        return this.StackView(new EditCouponAmountViewModel
        {
            Id = id, ChannelId = channelId, Code = coupon.Code, ChannelName = coupon.DistributionChannel!.AgencyName,
            AmountInFen = coupon.AmountInFen
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> EditCouponAmount(Guid id, Guid channelId, EditCouponAmountViewModel model)
    {
        var coupon = await couponService.GetByIdAsync(id);
        if (coupon == null || coupon.DistributionChannelId != channelId) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                await couponService.UpdateAmountAsync(id, model.AmountInFen!.Value);
                TempData["SuccessMessage"] = localizer["Coupon amount saved. New orders use the new amount; existing orders and purchase history are unchanged."].Value;
                return RedirectToAction(nameof(Details), new { id = channelId });
            }
            catch (InvalidOperationException)
            {
                ModelState.AddModelError(string.Empty, localizer["The discount cannot exceed the price of any applicable product. Enter a smaller amount."]);
            }
        }
        // Identity and navigation come from the stored coupon, never posted labels.
        model.Id = id;
        model.ChannelId = channelId;
        model.Code = coupon.Code;
        model.ChannelName = coupon.DistributionChannel!.AgencyName;
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> SetCouponEnabled(Guid id, Guid channelId, bool? isEnabled, bool returnToReview = false)
    {
        if (!ModelState.IsValid || !isEnabled.HasValue) return BadRequest();
        var coupon = await couponService.GetByIdAsync(id);
        if (coupon == null || coupon.DistributionChannelId != channelId) return NotFound();

        // Send the desired state rather than toggling: retrying a request must
        // never accidentally re-enable a coupon that was just disabled.
        await couponService.SetEnabledAsync(id, isEnabled.Value);
        TempData["SuccessMessage"] = !isEnabled.Value
            ? localizer["Coupon disabled. Existing claims, orders and memberships have been kept."].Value
            : coupon.DistributionChannel is { IsEnabled: false }
                ? localizer["Coupon enabled, but the channel is disabled. It remains unavailable until the channel is enabled."].Value
                : localizer["Coupon enabled. Single-use history has not been reset."].Value;
        return returnToReview
            ? RedirectToAction(nameof(DeleteCoupon), new { id, channelId })
            : RedirectToAction(nameof(Details), new { id = channelId });
    }

    [HttpGet]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> DeleteCoupon(Guid id, Guid channelId)
    {
        var coupon = await couponService.GetByIdAsync(id);
        if (coupon == null || coupon.DistributionChannelId != channelId) return NotFound();
        return this.StackView(new DeleteCouponViewModel
        {
            Coupon = coupon,
            Deletion = await couponService.GetDeletionSummaryAsync(id)
        }, nameof(DeleteCoupon));
    }

    [HttpPost]
    [ActionName(nameof(DeleteCoupon))]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> DeleteCouponConfirmed(Guid id, Guid channelId)
    {
        var coupon = await couponService.GetByIdAsync(id);
        if (coupon == null || coupon.DistributionChannelId != channelId) return NotFound();
        try
        {
            await couponService.DeleteAsync(id);
            return RedirectToAction(nameof(Details), new { id = channelId });
        }
        catch (Exception ex) when (IsDeletionConflict(ex))
        {
            dbContext.ChangeTracker.Clear();
            ModelState.AddModelError(string.Empty, localizer["This coupon has history and cannot be deleted. Its orders, claims and membership records must be preserved."]);
            return await DeleteCoupon(id, channelId);
        }
    }

    // Only known reference conflicts are business rejections. Unexpected database
    // or programming errors must remain visible to error monitoring.
    private static bool IsDeletionConflict(Exception exception) =>
        exception is InvalidOperationException { Message: CouponDeletionSummary.HistoryExists } ||
        exception is DbUpdateException { InnerException: MySqlException { Number: 1451 } mysql } &&
        (mysql.Message.Contains("`FK_PaymentOrders_Coupons_CouponId`", StringComparison.Ordinal) ||
         mysql.Message.Contains("`FK_Changes_Coupons_CouponId`", StringComparison.Ordinal));
}
