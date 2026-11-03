using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Views.Shared.Components.InstitutionSales;

public class InstitutionSales(WeChatExamDbContext context, IAuthorizationService authorization) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid? institutionId = null, Guid? realPaperId = null)
    {
        // A paper form can contain an unsaved institution change. Show the persisted scope.
        if (realPaperId.HasValue)
        {
            institutionId = await context.RealPapers.AsNoTracking()
                .Where(p => p.Id == realPaperId.Value)
                .Select(p => (Guid?)p.InstitutionId)
                .SingleOrDefaultAsync();
        }

        if (!institutionId.HasValue) return Content(string.Empty);
        var institution = await context.Institutions.AsNoTracking()
            .Where(i => i.Id == institutionId.Value)
            .Select(i => new { i.Id, i.Title })
            .SingleOrDefaultAsync();
        if (institution == null) return Content(string.Empty);

        var products = await context.VipProducts.AsNoTracking()
            .Where(p => p.Type == VipProductType.Institution && p.InstitutionId == institution.Id)
            .OrderByDescending(p => p.IsEnabled).ThenBy(p => p.Name)
            .ToListAsync();
        var canRead = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanReadVipProducts)).Succeeded;
        var canAdd = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanAddVipProducts)).Succeeded;
        var canEdit = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanEditVipProducts)).Succeeded;

        return View(new InstitutionSalesViewModel
        {
            InstitutionId = institution.Id,
            InstitutionTitle = institution.Title,
            Products = products,
            CanReadProducts = canRead,
            CanAddProducts = canAdd,
            CanEditProducts = canEdit
        });
    }
}

public class InstitutionSalesViewModel
{
    public Guid InstitutionId { get; init; }
    public string InstitutionTitle { get; init; } = string.Empty;
    public IReadOnlyList<VipProduct> Products { get; init; } = [];
    public bool CanReadProducts { get; init; }
    public bool CanAddProducts { get; init; }
    public bool CanEditProducts { get; init; }
}
