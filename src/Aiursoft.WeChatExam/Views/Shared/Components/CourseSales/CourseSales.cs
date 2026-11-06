using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Views.Shared.Components.CourseSales;

public class CourseSales(WeChatExamDbContext context, IAuthorizationService authorization) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid totalCategoryId)
    {
        var course = await context.TotalCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == totalCategoryId);
        if (course == null) return Content(string.Empty);
        var products = await context.VipProducts.AsNoTracking()
            .Where(p => p.Type == VipProductType.Category && p.TotalCategoryId == course.Id)
            .OrderByDescending(p => p.IsEnabled).ThenBy(p => p.Name).ToListAsync();
        var canRead = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanReadVipProducts)).Succeeded;
        var canAdd = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanAddVipProducts)).Succeeded;
        var canEdit = (await authorization.AuthorizeAsync(ViewContext.HttpContext.User,
            AppPermissionNames.CanEditVipProducts)).Succeeded;

        return View(new CourseSalesViewModel
        {
            TotalCategoryId = course.Id,
            CourseTitle = course.Title,
            Products = products,
            CanReadProducts = canRead,
            CanAddProducts = canAdd,
            CanEditProducts = canEdit
        });
    }
}

public class CourseSalesViewModel
{
    public Guid TotalCategoryId { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public IReadOnlyList<VipProduct> Products { get; init; } = [];
    public bool CanReadProducts { get; init; }
    public bool CanAddProducts { get; init; }
    public bool CanEditProducts { get; init; }
}
