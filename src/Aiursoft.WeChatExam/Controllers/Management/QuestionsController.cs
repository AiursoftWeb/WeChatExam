using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.QuestionsViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle questions related actions like create, edit, delete, etc.
/// </summary>
[LimitPerMin]
public class QuestionsController(TemplateDbContext context) : Controller
{
    // GET: questions
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Questions",
        LinkOrder = 2)]
    public async Task<IActionResult> Index(Guid? categoryId)
    {
        var categories = await context.Categories.ToListAsync();
        
        var questions = categoryId.HasValue
            ? await context.Questions
                .Where(q => q.CategoryId == categoryId.Value)
                .OrderByDescending(q => q.CreationTime)
                .ToListAsync()
            : new List<Question>();

        var selectedCategory = categoryId.HasValue
            ? await context.Categories.FirstOrDefaultAsync(c => c.Id == categoryId.Value)
            : null;

        return this.StackView(new IndexViewModel
        {
            Questions = questions,
            Categories = categories,
            SelectedCategoryId = categoryId,
            SelectedCategory = selectedCategory
        });
    }

    // GET: questions/create
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    public async Task<IActionResult> Create(Guid? categoryId)
    {
        var categories = await context.Categories.ToListAsync();
        
        var model = new CreateViewModel
        {
            Categories = categories,
            CategoryId = categoryId ?? Guid.Empty
        };
        
        return this.StackView(model);
    }

    // POST: questions
    [Authorize(Policy = AppPermissionNames.CanAddQuestions)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.Categories = await context.Categories.ToListAsync();
            return this.StackView(model);
        }

        // 验证分类是否存在
        var categoryExists = await context.Categories
            .AnyAsync(c => c.Id == model.CategoryId);

        if (!categoryExists)
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Category not found");
            model.Categories = await context.Categories.ToListAsync();
            return this.StackView(model);
        }

        var question = new Question
        {
            Id = Guid.NewGuid(),
            Type = model.Type,
            Text = model.Text,
            List = model.List,
            SingleCorrect = model.SingleCorrect,
            FillInCorrect = model.FillInCorrect,
            Explanation = model.Explanation,
            CategoryId = model.CategoryId
        };

        context.Questions.Add(question);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = question.Id });
    }

    // GET: questions/{id}
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var question = await context.Questions
            .Include(q => q.Category)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            Question = question,
            Category = question.Category
        });
    }

    // GET: questions/{id}/edit
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();

        var question = await context.Questions.FindAsync(id);
        if (question == null) return NotFound();

        var categories = await context.Categories.ToListAsync();

        var model = new EditViewModel
        {
            Id = id.Value,
            Type = question.Type,
            Text = question.Text,
            List = question.List,
            SingleCorrect = question.SingleCorrect,
            FillInCorrect = question.FillInCorrect,
            Explanation = question.Explanation,
            CategoryId = question.CategoryId,
            Categories = categories
        };

        return this.StackView(model);
    }

    // POST: questions/{id}
    [Authorize(Policy = AppPermissionNames.CanEditQuestions)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            model.Categories = await context.Categories.ToListAsync();
            return this.StackView(model);
        }

        var question = await context.Questions.FindAsync(id);
        if (question == null) return NotFound();

        // 验证分类是否存在
        var categoryExists = await context.Categories
            .AnyAsync(c => c.Id == model.CategoryId);

        if (!categoryExists)
        {
            ModelState.AddModelError(nameof(model.CategoryId), "Category not found");
            model.Categories = await context.Categories.ToListAsync();
            return this.StackView(model);
        }

        question.Type = model.Type;
        question.Text = model.Text;
        question.List = model.List;
        question.SingleCorrect = model.SingleCorrect;
        question.FillInCorrect = model.FillInCorrect;
        question.Explanation = model.Explanation;
        question.CategoryId = model.CategoryId;

        context.Questions.Update(question);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = question.Id });
    }

    // GET: questions/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteQuestions)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var question = await context.Questions
            .Include(q => q.Category)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (question == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            Question = question,
            Category = question.Category
        });
    }

    // POST: questions/{id}/delete
    [Authorize(Policy = AppPermissionNames.CanDeleteQuestions)]
    [HttpPost("delete/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var question = await context.Questions.FindAsync(id);
        if (question == null) return NotFound();

        var categoryId = question.CategoryId;

        context.Questions.Remove(question);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index), new { categoryId });
    }
}
