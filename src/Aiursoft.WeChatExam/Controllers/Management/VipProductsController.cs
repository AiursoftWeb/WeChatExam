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

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize]
[LimitPerMin]
public class VipProductsController(
    IVipProductService vipProductService,
    WeChatExamDbContext dbContext) : Controller
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
                            (p.Category?.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
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
        var categories = await dbContext.Categories.OrderBy(c => c.Title).ToListAsync();
        return this.StackView(new CreateViewModel
        {
            Categories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanAddVipProducts)]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var categories = await dbContext.Categories.OrderBy(c => c.Title).ToListAsync();
            model.Categories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList();
            return this.StackView(model);
        }

        await vipProductService.CreateAsync(model.Name, model.CategoryId, model.PriceInFen, model.DurationDays);
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Policy = AppPermissionNames.CanEditVipProducts)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();

        var product = await vipProductService.GetByIdAsync(id.Value);
        if (product == null) return NotFound();

        var categories = await dbContext.Categories.OrderBy(c => c.Title).ToListAsync();
        return this.StackView(new EditViewModel
        {
            Id = product.Id,
            Name = product.Name,
            CategoryId = product.CategoryId,
            PriceInFen = product.PriceInFen,
            DurationDays = product.DurationDays,
            IsEnabled = product.IsEnabled,
            Categories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditVipProducts)]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var categories = await dbContext.Categories.OrderBy(c => c.Title).ToListAsync();
            model.Categories = categories.Select(c => new SelectListItem(c.Title, c.Id.ToString())).ToList();
            return this.StackView(model);
        }

        await vipProductService.UpdateAsync(id, model.Name, model.CategoryId, model.PriceInFen, model.DurationDays, model.IsEnabled);
        return RedirectToAction(nameof(Index));
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
