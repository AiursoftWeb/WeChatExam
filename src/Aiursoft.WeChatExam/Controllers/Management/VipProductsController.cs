using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.VipProductsViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class VipProductsController(
    IVipProductService vipProductService,
    WeChatExamDbContext dbContext,
    IStringLocalizer<VipProductsController> localizer) : Controller
{
    [Authorize(Policy = AppPermissionNames.CanReadVipProducts)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Payment Management",
        CascadedLinksIcon = "credit-card",
        CascadedLinksOrder = 3,
        LinkText = "VIP Products",
        LinkOrder = 0)]
    public async Task<IActionResult> Index(string? search)
    {
        var products = await vipProductService.GetAllAsync();

        if (!string.IsNullOrWhiteSpace(search))
        {
            products = products
                .Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            (p.TotalCategory?.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();
        }

        return this.StackView(new IndexViewModel
        {
            Products = products,
            SearchQuery = search
        });
    }

    [Authorize(Policy = AppPermissionNames.CanAddVipProducts)]
    public async Task<IActionResult> Create()
    {
        var categories = await dbContext.TotalCategories.OrderBy(c => c.Title).ToListAsync();
        return this.StackView(new CreateViewModel
        {
            TotalCategories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAddVipProducts)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        await ValidateTotalCategoryAsync(model.Type, model.TotalCategoryId);
        if (!ModelState.IsValid)
        {
            var categories = await dbContext.TotalCategories.OrderBy(c => c.Title).ToListAsync();
            model.TotalCategories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList();
            return this.StackView(model);
        }

        await vipProductService.CreateAsync(model.Name, model.Type, model.TotalCategoryId, model.PriceInFen, model.DurationDays);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanEditVipProducts)]
    public async Task<IActionResult> Edit(Guid? id, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id == null) return NotFound();

        var product = await vipProductService.GetByIdAsync(id.Value);
        if (product == null) return NotFound();

        var categories = await dbContext.TotalCategories.OrderBy(c => c.Title).ToListAsync();
        return this.StackView(new EditViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Type = product.Type,
            TotalCategoryId = product.TotalCategoryId,
            PriceInFen = product.PriceInFen,
            DurationDays = product.DurationDays,
            IsEnabled = product.IsEnabled,
            TotalCategories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditVipProducts)]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        await ValidateTotalCategoryAsync(model.Type, model.TotalCategoryId);
        if (!ModelState.IsValid)
        {
            var categories = await dbContext.TotalCategories.OrderBy(c => c.Title).ToListAsync();
            model.TotalCategories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList();
            return this.StackView(model);
        }

        await vipProductService.UpdateAsync(id, model.Name, model.Type, model.TotalCategoryId, model.PriceInFen, model.DurationDays, model.IsEnabled);
        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);

        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateTotalCategoryAsync(VipProductType type, Guid? totalCategoryId)
    {
        if (type == VipProductType.Category &&
            (!totalCategoryId.HasValue || !await dbContext.TotalCategories.AnyAsync(c => c.Id == totalCategoryId.Value)))
        {
            ModelState.AddModelError("TotalCategoryId", localizer["Select an existing total category."]);
        }
    }

    [Authorize(Policy = AppPermissionNames.CanDeleteVipProducts)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var product = await vipProductService.GetByIdAsync(id.Value);
        if (product == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            Product = product
        });
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteVipProducts)]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        await vipProductService.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }
}
