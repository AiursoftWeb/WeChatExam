using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.QuestionsViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle questions related actions like create, edit, delete, etc.
/// </summary>
[LimitPerMin]
public class QuestionsController(WeChatExamDbContext context, ITagService tagService) : Controller
{
    // GET: questions
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Questions",
        LinkOrder = 2)]
    public async Task<IActionResult> Index(
        Guid? categoryId,
        QuestionType? questionType,
        DateTime? startDate,
        DateTime? endDate,
        string? tag,
        string? mtql,
        string sortBy = "CreatedAt",
        string sortOrder = "Desc",
        int page = 1,
        int pageSize = 20)
    {
        var query = context.Questions.AsQueryable();

        // Filter by Category
        if (categoryId.HasValue)
        {
            query = query.Where(q => q.CategoryId == categoryId.Value);
        }

        // Filter by QuestionType
        if (questionType.HasValue)
        {
            query = query.Where(q => q.QuestionType == questionType.Value);
        }

        // Filter by Date Range
        if (startDate.HasValue)
        {
            query = query.Where(q => q.CreationTime >= startDate.Value.ToUniversalTime());
        }
        if (endDate.HasValue)
        {
            // Add 1 day to include the end date
            query = query.Where(q => q.CreationTime < endDate.Value.AddDays(1).ToUniversalTime());
        }

        // Filter by MTQL (Priority) or Tag
        if (!string.IsNullOrWhiteSpace(mtql))
        {
            try
            {
                var tokens = MTQL.Services.Tokenizer.Tokenize(mtql);
                var rpn = MTQL.Services.Parser.ToRpn(tokens);
                var ast = MTQL.Services.AstBuilder.Build(rpn);
                var predicate = MTQL.Services.PredicateBuilder.Build(ast);
                query = query.Where(predicate);
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(nameof(mtql), $"Invalid MTQL: {ex.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalizedTag = tag.Trim().ToUpperInvariant();
            query = query.Where(q => q.QuestionTags.Any(qt => qt.Tag.NormalizedName == normalizedTag));
        }

        // Sorting
        query = (sortBy, sortOrder.ToLower()) switch
        {
            ("CreatedAt", "asc") => query.OrderBy(q => q.CreationTime),
            ("CreatedAt", "desc") => query.OrderByDescending(q => q.CreationTime),
            ("QuestionType", "asc") => query.OrderBy(q => q.QuestionType),
            ("QuestionType", "desc") => query.OrderByDescending(q => q.QuestionType),
            _ => query.OrderByDescending(q => q.CreationTime)
        };

        // Pagination
        var totalCount = await query.CountAsync();
        var questions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var categories = await context.Categories.OrderBy(c => c.Title).ToListAsync();
        
        // Prepare Dropdown Options
        var questionTypeOptions = Enum.GetValues<QuestionType>()
            .Select(t => new SelectListItem
            {
                Value = t.ToString(),
                Text = t.ToString(),
                Selected = questionType == t
            });

        var pageSizeOptions = new List<SelectListItem>
        {
            new SelectListItem { Value = "10", Text = "10", Selected = pageSize == 10 },
            new SelectListItem { Value = "20", Text = "20", Selected = pageSize == 20 },
            new SelectListItem { Value = "50", Text = "50", Selected = pageSize == 50 },
            new SelectListItem { Value = "100", Text = "100", Selected = pageSize == 100 }
        };

        return this.StackView(new IndexViewModel
        {
            Questions = questions,
            Categories = categories,
            SelectedCategoryId = categoryId,
            SelectedCategory = categories.FirstOrDefault(c => c.Id == categoryId),
            
            // Pagination
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            
            // Filters
            FilterQuestionType = questionType,
            FilterStartDate = startDate,
            FilterEndDate = endDate,
            FilterTag = tag,
            FilterMtql = mtql,
            
            // Sorting
            SortBy = sortBy,
            SortOrder = sortOrder,
            
            // Options
            QuestionTypeOptions = questionTypeOptions,
            PageSizeOptions = pageSizeOptions
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
            Content = model.Content,
            QuestionType = model.QuestionType,
            GradingStrategy = model.GradingStrategy,
            Metadata = model.Metadata,
            StandardAnswer = model.StandardAnswer,
            Explanation = model.Explanation,
            CategoryId = model.CategoryId
        };

        context.Questions.Add(question);
        await context.SaveChangesAsync();

        // Process tags
        if (!string.IsNullOrWhiteSpace(model.Tags))
        {
            var tagNames = model.Tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            foreach (var tagName in tagNames)
            {
                var tag = await tagService.AddTagAsync(tagName);
                await tagService.AddTagToQuestionAsync(question.Id, tag.Id);
            }
        }

        return RedirectToAction(nameof(Details), new { id = question.Id });
    }

    // GET: questions/{id}
    [Authorize(Policy = AppPermissionNames.CanReadQuestions)]
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var question = await context.Questions
            .Include(q => q.Category)
            .Include(q => q.QuestionTags)
            .ThenInclude(qt => qt.Tag)
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
        var tags = await tagService.GetTagsForQuestionAsync(id.Value);

        var model = new EditViewModel
        {
            Id = id.Value,
            Content = question.Content,
            QuestionType = question.QuestionType,
            GradingStrategy = question.GradingStrategy,
            Metadata = question.Metadata,
            StandardAnswer = question.StandardAnswer,
            Explanation = question.Explanation,
            CategoryId = question.CategoryId ?? Guid.Empty,
            Categories = categories,
            Tags = string.Join(" ", tags.Select(t => t.DisplayName))
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

        question.Content = model.Content;
        question.QuestionType = model.QuestionType;
        question.GradingStrategy = model.GradingStrategy;
        question.Metadata = model.Metadata;
        question.StandardAnswer = model.StandardAnswer;
        question.Explanation = model.Explanation;
        question.CategoryId = model.CategoryId;

        context.Questions.Update(question);
        await context.SaveChangesAsync();

        // Process tags
        if (!string.IsNullOrWhiteSpace(model.Tags))
        {
            var tagNames = model.Tags.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
            var currentTags = await tagService.GetTagsForQuestionAsync(id);

            // Remove tags not in new list
            foreach (var t in currentTags)
            {
                if (!tagNames.Contains(t.DisplayName))
                {
                    await tagService.RemoveTagFromQuestionAsync(id, t.Id);
                }
            }

            // Add new tags
            foreach (var tagName in tagNames)
            {
                if (!currentTags.Any(t => t.DisplayName == tagName))
                {
                    var tag = await tagService.AddTagAsync(tagName);
                    await tagService.AddTagToQuestionAsync(id, tag.Id);
                }
            }
        }

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
    [HttpPost]
    [ActionName("Delete")]
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

    // POST: questions/batch-delete
    [Authorize(Policy = AppPermissionNames.CanDeleteQuestions)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BatchDelete([FromBody] BatchDeleteRequest request)
    {
        if (!request.QuestionIds.Any())
        {
            return BadRequest("No questions selected for deletion.");
        }

        var questionsToDelete = await context.Questions
            .Where(q => request.QuestionIds.Contains(q.Id))
            .ToListAsync();

        if (questionsToDelete.Any())
        {
            context.Questions.RemoveRange(questionsToDelete);
            await context.SaveChangesAsync();
        }

        return Json(new BatchDeleteResult 
        { 
            DeletedCount = questionsToDelete.Count,
            DeletedIds = questionsToDelete.Select(q => q.Id).ToArray()
        });
    }
}
