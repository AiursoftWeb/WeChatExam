using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.VipMembersViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class VipMembersController(
    WeChatExamDbContext dbContext,
    UserManager<User> userManager) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 3,
        LinkText = "VIP Members",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? userId, string? search, int page = 1, int pageSize = 15)
    {
        var query = dbContext.VipMemberships
            .Include(v => v.User)
            .Include(v => v.VipProduct)
            .AsQueryable();

        User? targetUser = null;
        if (!string.IsNullOrWhiteSpace(userId))
        {
            targetUser = await userManager.FindByIdAsync(userId);
            if (targetUser != null)
            {
                query = query.Where(v => v.UserId == userId);
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(v =>
                (v.User != null && (v.User.UserName!.Contains(search) || v.User.DisplayName.Contains(search))) ||
                (v.VipProduct != null && v.VipProduct.Name.Contains(search))
            );
        }

        var totalCount = await query.CountAsync();
        var memberships = await query.OrderByDescending(v => v.EndTime)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return this.StackView(new IndexViewModel
        {
            VipMembers = memberships,
            SearchQuery = search,
            UserId = userId,
            TargetUser = targetUser,
            TotalCount = totalCount,
            CurrentPage = page,
            PageSize = pageSize
        });
    }

    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Create(string? userId)
    {
        var products = await dbContext.VipProducts.OrderBy(p => p.Name).ToListAsync();
        var model = new CreateViewModel
        {
            VipProducts = products.Select(p => new SelectListItem($"{p.Name} ({p.DurationDays} days)", p.Id.ToString())).ToList(),
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddYears(1)
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user != null)
            {
                model.UserId = user.Id;
                model.UserName = user.DisplayName;
            }
        }

        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        var user = await userManager.FindByIdAsync(model.UserId);
        if (user == null)
        {
            ModelState.AddModelError(nameof(model.UserId), "User not found.");
        }

        if (!ModelState.IsValid)
        {
            var products = await dbContext.VipProducts.OrderBy(p => p.Name).ToListAsync();
            model.VipProducts = products.Select(p => new SelectListItem($"{p.Name} ({p.DurationDays} days)", p.Id.ToString())).ToList();
            if (user != null) model.UserName = user.DisplayName;
            return this.StackView(model);
        }

        var membership = new VipMembership
        {
            UserId = model.UserId,
            VipProductId = model.VipProductId,
            StartTime = model.StartTime.ToUniversalTime(),
            EndTime = model.EndTime.ToUniversalTime()
        };

        dbContext.VipMemberships.Add(membership);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { userId = model.UserId });
    }

    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();

        var membership = await dbContext.VipMemberships
            .Include(v => v.User)
            .Include(v => v.VipProduct)
            .FirstOrDefaultAsync(v => v.Id == id.Value);

        if (membership == null) return NotFound();

        return this.StackView(new EditViewModel
        {
            Id = membership.Id,
            StartTime = membership.StartTime.ToLocalTime(),
            EndTime = membership.EndTime.ToLocalTime(),
            UserName = membership.User?.DisplayName,
            ProductName = membership.VipProduct?.Name
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var membership = await dbContext.VipMemberships.FirstOrDefaultAsync(v => v.Id == id);
        if (membership == null) return NotFound();

        membership.StartTime = model.StartTime.ToUniversalTime();
        membership.EndTime = model.EndTime.ToUniversalTime();

        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { userId = membership.UserId });
    }

    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var membership = await dbContext.VipMemberships
            .Include(v => v.User)
            .Include(v => v.VipProduct)
            .FirstOrDefaultAsync(v => v.Id == id.Value);

        if (membership == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            VipMembership = membership
        });
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanManageVipMembers)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var membership = await dbContext.VipMemberships.FirstOrDefaultAsync(v => v.Id == id);
        if (membership == null) return NotFound();

        dbContext.VipMemberships.Remove(membership);
        await dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { userId = membership.UserId });
    }
}
