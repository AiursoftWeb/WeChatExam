using Aiursoft.UiStack.Navigation;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.QuestionImportViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanAddQuestions)]
public class QuestionImportController : Controller
{
    private readonly WeChatExamDbContext _dbContext;
    private readonly ITagService _tagService;

    public QuestionImportController(
        WeChatExamDbContext dbContext,
        ITagService tagService)
    {
        _dbContext = dbContext;
        _tagService = tagService;
    }

    /// <summary>
    /// GET: QuestionImport - Show JSON input form with QuestionType dropdown
    /// </summary>
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Import Questions",
        LinkOrder = 4)]
    public IActionResult Index(QuestionImportIndexViewModel? model)
    {
        model ??= new QuestionImportIndexViewModel();
        return this.StackView(model);
    }

    /// <summary>
    /// POST: Preview - Parse JSON and show preview table
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Preview(QuestionImportIndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model, nameof(Index));
        }

        try
        {
            var data = JsonConvert.DeserializeObject<List<ImportedQuestionItem>>(model.JsonContent);
            if (data == null || data.Count == 0)
            {
                throw new Exception("No questions found in JSON.");
            }

            // For Bool type, auto-fill Options if empty
            if (model.SelectedQuestionType == QuestionType.Bool)
            {
                foreach (var item in data)
                {
                    item.Options = new List<string> { "正确", "错误" };
                }
            }

            // For Choice type, validate that all questions have options
            if (model.SelectedQuestionType == QuestionType.Choice)
            {
                var questionsWithoutOptions = data
                    .Select((item, index) => new { Item = item, Index = index + 1 })
                    .Where(x => x.Item.Options.Count == 0)
                    .ToList();

                if (questionsWithoutOptions.Any())
                {
                    var missingIndices = string.Join(", ", questionsWithoutOptions.Select(x => $"#{x.Index}"));
                    model.ErrorMessage = $"选择题必须提供选项 (Options)。以下题目缺少选项: {missingIndices}";
                    return this.StackView(model, nameof(Index));
                }
            }

            var previewModel = new QuestionImportPreviewViewModel
            {
                JsonContent = model.JsonContent,
                SelectedQuestionType = model.SelectedQuestionType,
                ParsedQuestions = data
            };

            return this.StackView(previewModel, "Preview");
        }
        catch (Exception e)
        {
            model.ErrorMessage = $"JSON Parse Error: {e.Message}";
            return this.StackView(model, nameof(Index));
        }
    }

    /// <summary>
    /// POST: Submit - Save questions to database
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(QuestionImportPreviewViewModel model)
    {
        try
        {
            var data = JsonConvert.DeserializeObject<List<ImportedQuestionItem>>(model.JsonContent);
            if (data == null || data.Count == 0)
            {
                throw new Exception("No questions found.");
            }

            // Determine grading strategy based on question type
            var gradingStrategy = model.SelectedQuestionType switch
            {
                QuestionType.Choice => GradingStrategy.ExactMatch,
                QuestionType.Bool => GradingStrategy.ExactMatch,
                QuestionType.Blank => GradingStrategy.FuzzyMatch,
                _ => GradingStrategy.AiEval
            };

            var importedCount = 0;
            foreach (var item in data)
            {
                // For Bool type, force options to ["正确", "错误"]
                var options = model.SelectedQuestionType == QuestionType.Bool
                    ? new List<string> { "正确", "错误" }
                    : item.Options;

                // Build metadata JSON for Choice/Bool types
                var metadata = (model.SelectedQuestionType == QuestionType.Choice ||
                               model.SelectedQuestionType == QuestionType.Bool) && options.Count > 0
                    ? JsonConvert.SerializeObject(new { options })
                    : string.Empty;

                // Create question entity
                var question = new Question
                {
                    Id = Guid.NewGuid(),
                    Content = item.Question,
                    QuestionType = model.SelectedQuestionType,
                    GradingStrategy = gradingStrategy,
                    Metadata = metadata,
                    StandardAnswer = item.Answer,
                    Explanation = item.Analysis,
                    CategoryId = null // 未分类
                };

                _dbContext.Questions.Add(question);

                // Handle OriginalFilename as tag
                if (!string.IsNullOrWhiteSpace(item.OriginalFilename))
                {
                    var tag = await _tagService.AddTagAsync(item.OriginalFilename.Trim());
                    var exists = await _dbContext.QuestionTags
                        .AnyAsync(qt => qt.QuestionId == question.Id && qt.TagId == tag.Id);

                    if (!exists)
                    {
                        var questionTag = new QuestionTag
                        {
                            QuestionId = question.Id,
                            TagId = tag.Id
                        };
                        _dbContext.QuestionTags.Add(questionTag);
                    }
                }

                importedCount++;
            }

            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Successfully imported {importedCount} questions.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception e)
        {
            // Re-parse for preview display
            try
            {
                var data = JsonConvert.DeserializeObject<List<ImportedQuestionItem>>(model.JsonContent) ?? new();

                // For Bool type, auto-fill Options
                if (model.SelectedQuestionType == QuestionType.Bool)
                {
                    foreach (var item in data)
                    {
                        item.Options = new List<string> { "正确", "错误" };
                    }
                }

                model.ParsedQuestions = data;
            }
            catch
            {
                model.ParsedQuestions = new List<ImportedQuestionItem>();
            }

            model.ErrorMessage = $"Save Error: {e.Message}";
            return this.StackView(model, "Preview");
        }
    }

    /// <summary>
    /// POST: ResumeEdit - Go back to edit JSON from preview
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ResumeEdit(QuestionImportPreviewViewModel model)
    {
        var editModel = new QuestionImportIndexViewModel
        {
            JsonContent = model.JsonContent,
            SelectedQuestionType = model.SelectedQuestionType
        };
        return this.StackView(editModel, nameof(Index));
    }
}
