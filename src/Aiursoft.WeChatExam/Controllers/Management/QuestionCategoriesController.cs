using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.QuestionCategoriesViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanEditAnyCategory)]
public class QuestionCategoriesController(
    WeChatExamDbContext context,
    KnowledgeExerciseValidation exerciseValidation,
    IStringLocalizer<QuestionCategoriesController> localizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Question Bank Management",
        CascadedLinksIcon = "library-big",
        CascadedLinksOrder = 2,
        LinkText = "Periods",
        LinkOrder = 0)]
    public async Task<IActionResult> Index(Guid? totalCategoryId)
    {
        var totals = await GetTotalCategories();
        totalCategoryId ??= totals.FirstOrDefault()?.Id;
        if (totalCategoryId.HasValue && totals.All(c => c.Id != totalCategoryId.Value)) return NotFound();

        return this.StackView(new IndexViewModel
        {
            TotalCategories = totals,
            TotalCategoryId = totalCategoryId,
            Categories = await context.QuestionCategories.AsNoTracking()
                .Where(c => c.TotalCategoryId == totalCategoryId)
                .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync()
        });
    }

    public async Task<IActionResult> Create(Guid? totalCategoryId)
    {
        var totals = await GetTotalCategories();
        if (totalCategoryId.HasValue && totals.All(c => c.Id != totalCategoryId.Value)) return NotFound();
        return this.StackView(new EditViewModel
        {
            PageTitle = "Create Period",
            TotalCategoryId = totalCategoryId,
            TotalCategories = totals
        }, nameof(Edit));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(EditViewModel model)
    {
        model.Id = Guid.Empty;
        ModelState.Remove(nameof(model.Id));
        model.PageTitle = "Create Period";
        await ValidateTotalCategory(model);
        if (!ModelState.IsValid) return this.StackView(model, nameof(Edit));

        context.QuestionCategories.Add(new QuestionCategory
        {
            Title = model.Title.Trim(),
            TotalCategoryId = model.TotalCategoryId!.Value,
            IsFree = model.IsFree,
            OrderIndex = model.OrderIndex
        });
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { totalCategoryId = model.TotalCategoryId });
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var category = await context.QuestionCategories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        return this.StackView(new EditViewModel
        {
            Id = category.Id,
            Title = category.Title,
            TotalCategoryId = category.TotalCategoryId,
            IsFree = category.IsFree,
            OrderIndex = category.OrderIndex,
            TotalCategories = await GetTotalCategories()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();
        var category = await context.QuestionCategories.SingleOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();
        await ValidateTotalCategory(model);
        if (!model.IsFree && category.IsFree)
        {
            var error = await exerciseValidation.ValidatePaidQuestionCategoryAsync(id);
            if (error != null) ModelState.AddModelError(nameof(model.IsFree), error);
        }
        if (!ModelState.IsValid) return this.StackView(model);

        category.Title = model.Title.Trim();
        category.TotalCategoryId = model.TotalCategoryId!.Value;
        category.IsFree = model.IsFree;
        category.OrderIndex = model.OrderIndex;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { totalCategoryId = model.TotalCategoryId });
    }

    private Task<List<TotalCategory>> GetTotalCategories() => context.TotalCategories.AsNoTracking()
        .OrderBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();

    private async Task ValidateTotalCategory(EditViewModel model)
    {
        model.TotalCategories = await GetTotalCategories();
        if (model.TotalCategoryId.HasValue && model.TotalCategories.All(c => c.Id != model.TotalCategoryId.Value))
        {
            ModelState.AddModelError(nameof(model.TotalCategoryId), localizer["Choose an existing total category."]);
        }
    }
}
