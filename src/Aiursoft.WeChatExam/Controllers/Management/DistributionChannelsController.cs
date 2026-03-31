using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.DistributionChannelsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class DistributionChannelsController(
    IDistributionChannelService distributionChannelService,
    ICouponService couponService,
    IVipProductService vipProductService) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanReadDistributionChannels)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Marketing Management",
        CascadedLinksIcon = "megaphone",
        CascadedLinksOrder = 4,
        LinkText = "Distribution Channels",
        LinkOrder = 2)]
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
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var channel = await distributionChannelService.GetByIdAsync(id.Value);
        if (channel == null) return NotFound();

        var stats = await distributionChannelService.GetStatsAsync(id.Value);

        return this.StackView(new DetailsViewModel
        {
            Channel = channel,
            Stats = stats
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> ToggleEnabled(Guid id)
    {
        var channel = await distributionChannelService.GetByIdAsync(id);
        if (channel == null) return NotFound();

        await distributionChannelService.SetEnabledAsync(id, !channel.IsEnabled);
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
            RegistrationCount = stats.RegistrationCount
        });
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteDistributionChannels)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        await distributionChannelService.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditDistributionChannels)]
    public async Task<IActionResult> DeleteCoupon(Guid id, Guid channelId)
    {
        await couponService.DeleteAsync(id);
        return RedirectToAction(nameof(Details), new { id = channelId });
    }
}
