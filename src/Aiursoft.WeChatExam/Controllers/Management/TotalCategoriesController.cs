using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.TotalCategoriesViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
public class TotalCategoriesController(WeChatExamDbContext context, IAuthorizationService authorization) : Controller
{
    private const int PreviewCount = 5;

    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Total Category Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 10,
        LinkText = "Total Categories",
        LinkOrder = 0)]
    public async Task<IActionResult> Index()
    {
        return this.StackView(new IndexViewModel
        {
            Categories = await context.TotalCategories.AsNoTracking()
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync()
        });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var course = await context.TotalCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
        if (course == null) return NotFound();

        var canReadQuestions = (await authorization.AuthorizeAsync(User, AppPermissionNames.CanReadQuestions)).Succeeded;
        var canEditPapers = (await authorization.AuthorizeAsync(User, AppPermissionNames.CanEditPapers)).Succeeded;
        return this.StackView(new DetailsViewModel
        {
            Course = course,
            CanReadQuestions = canReadQuestions,
            CanEditPapers = canEditPapers,
            PeriodCount = await context.QuestionCategories.CountAsync(c => c.TotalCategoryId == id),
            Periods = await context.QuestionCategories.AsNoTracking().Where(c => c.TotalCategoryId == id)
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id)
                .Take(PreviewCount).ToListAsync(),
            KnowledgeCategoryCount = await context.KnowledgeCategories.CountAsync(c => c.TotalCategoryId == id),
            RootKnowledgeCategories = await context.KnowledgeCategories.AsNoTracking()
                .Where(c => c.TotalCategoryId == id && c.ParentKnowledgeCategoryId == null)
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id)
                .Take(PreviewCount).ToListAsync(),
            MockPaperCount = canEditPapers ? await context.MockPapers.CountAsync(p => p.TotalCategoryId == id) : 0,
            MockPapers = canEditPapers ? await context.MockPapers.AsNoTracking().Where(p => p.TotalCategoryId == id)
                .OrderByDescending(p => p.CreationTime).ThenBy(p => p.Id)
                .Take(PreviewCount).ToListAsync() : []
        });
    }

    public IActionResult Create()
    {
        return this.StackView(new EditViewModel { PageTitle = "Create Total Category" }, nameof(Edit));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        // A posted ID must never turn creation into editing an existing course.
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Total Category";
        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Edit));
        }

        context.TotalCategories.Add(new TotalCategory
        {
            Title = model.Title.Trim(),
            OrderIndex = model.OrderIndex
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        var contents = await Contents().SingleOrDefaultAsync(c => c.Category.Id == id);
        if (contents == null) return NotFound();
        var category = contents.Category;

        return this.StackView(new EditViewModel
        {
            Id = category.Id,
            Title = category.Title,
            OrderIndex = category.OrderIndex,
            Contents = contents
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model, string? checkReturnUrl = null)
    {
        ViewData["CheckReturnUrl"] = Url.IsLocalUrl(checkReturnUrl) ? checkReturnUrl : null;
        if (id != model.Id) return NotFound();
        var category = await context.TotalCategories.SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        if (!ModelState.IsValid)
        {
            model.Contents = await Contents().SingleAsync(c => c.Category.Id == id);
            return this.StackView(model);
        }

        category.Title = model.Title.Trim();
        category.OrderIndex = model.OrderIndex;
        await context.SaveChangesAsync();
        if (Url.IsLocalUrl(checkReturnUrl)) return LocalRedirect(checkReturnUrl);
        return RedirectToAction(nameof(Index));
    }

    private IQueryable<ContentsViewModel> Contents() => context.TotalCategories.AsNoTracking()
        .Select(category => new ContentsViewModel
        {
            Category = category,
            PeriodCount = context.QuestionCategories.Count(c => c.TotalCategoryId == category.Id),
            QuestionCount = context.Questions.Count(q => q.QuestionCategory!.TotalCategoryId == category.Id),
            KnowledgeCategoryCount = context.KnowledgeCategories.Count(c => c.TotalCategoryId == category.Id),
            MockPaperCount = context.MockPapers.Count(p => p.TotalCategoryId == category.Id)
        });
}
