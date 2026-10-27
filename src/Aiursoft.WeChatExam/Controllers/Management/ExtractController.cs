using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.Canon.TaskQueue;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanUseAIExtractor)]
public class ExtractController : Controller
{
    private readonly IStringLocalizer<ExtractController> _localizer;
    private readonly IExtractService _extractService;
    private readonly WeChatExamDbContext _dbContext;
    private readonly ServiceTaskQueue _taskQueue;
    private readonly GlobalSettingsService _globalSettingsService;

    public ExtractController(
        IExtractService extractService,
        WeChatExamDbContext dbContext,
        ServiceTaskQueue taskQueue,
        GlobalSettingsService globalSettingsService,
        IStringLocalizer<ExtractController> localizer)
    {
        _localizer = localizer;
        _extractService = extractService;
        _dbContext = dbContext;
        _taskQueue = taskQueue;
        _globalSettingsService = globalSettingsService;
    }

    public async Task<IActionResult> Index(ExtractIndexViewModel? model)
    {
        model ??= new ExtractIndexViewModel();
        if (string.IsNullOrEmpty(model.SystemPrompt))
        {
            model.SystemPrompt = await _globalSettingsService.GetSettingValueAsync(SettingsMap.AiPromptExtractDefault);
        }

        await LoadDestinations(model);
        // Opening the form (or returning to the material) is not a submission.
        ModelState.Clear();
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extract(ExtractIndexViewModel model)
    {
        await ValidateDestinations(model);
        if (!ModelState.IsValid)
        {
            await LoadDestinations(model);
            return this.StackView(model, nameof(Index));
        }

        // The material form uses the configured prompt; it does not post a prompt field.
        if (string.IsNullOrWhiteSpace(model.SystemPrompt))
        {
            model.SystemPrompt = await _globalSettingsService.GetSettingValueAsync(SettingsMap.AiPromptExtractDefault);
        }
        var knowledgeCategoryId = model.KnowledgeCategoryId!.Value;
        var questionCategoryId = model.QuestionCategoryId!.Value;
        _taskQueue.QueueWithDependency<IExtractService>(
            queueName: "Extraction",
            taskName: $"AI Extraction to Category {model.KnowledgeCategoryId}",
            task: async (extractService) =>
            {
                var json = await extractService.GenerateJsonAsync(model.Material, model.SystemPrompt);
                var data = JsonConvert.DeserializeObject<List<ExtractedKnowledgePoint>>(json);
                if (data != null)
                {
                    await extractService.SaveAsync(data, knowledgeCategoryId, questionCategoryId);
                }
            });

        return RedirectToAction("Index", "Jobs");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(ExtractEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model, "Edit");
        }

        try
        {
            var data = JsonConvert.DeserializeObject<List<ExtractedKnowledgePoint>>(model.JsonContent);
            if (data == null)
            {
                throw new Exception("Extracted data is null.");
            }

            var previewModel = new ExtractPreviewViewModel
            {
                JsonContent = model.JsonContent,
                Data = data,
                OriginalMaterial = model.OriginalMaterial,
                KnowledgeCategoryId = model.KnowledgeCategoryId,
                QuestionCategoryId = model.QuestionCategoryId
            };

            await LoadDestinations(previewModel);
            return this.StackView(previewModel, "Preview");
        }
        catch (Exception e)
        {
            model.ErrorMessage = $"JSON Parse Error: {e.Message}";
            return this.StackView(model, "Edit");
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(ExtractPreviewViewModel model)
    {
        // Re-validate just in case, though we bind hidden JsonContent
        try
        {
            var data = JsonConvert.DeserializeObject<List<ExtractedKnowledgePoint>>(model.JsonContent);
            if (data == null)
            {
                throw new Exception("Data is corrupted.");
            }

            model.Data = data;
            await ValidateDestinations(model);
            if (!ModelState.IsValid)
            {
                await LoadDestinations(model);
                return this.StackView(model, "Preview");
            }
            await _extractService.SaveAsync(data, model.KnowledgeCategoryId!.Value, model.QuestionCategoryId!.Value);
            
            return RedirectToAction(nameof(Index)); // Or a success page
        }
        catch (Exception e)
        {
            await LoadDestinations(model);
            // We also need to re-deserialize data for display if we return View
            try {
                model.Data = JsonConvert.DeserializeObject<List<ExtractedKnowledgePoint>>(model.JsonContent) ?? new();
            } 
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            ModelState.AddModelError(string.Empty, $"Save Error: {e.Message}");
            return this.StackView(model, "Preview");
        }
    }

    private async Task LoadDestinations(ExtractDestinationViewModel model)
    {
        var categories = await _dbContext.KnowledgeCategories.AsNoTracking().Include(c => c.TotalCategory).OrderBy(c => c.Title).ToListAsync();
        var paths = KnowledgeCategoryPaths.Build(categories);
        model.Categories = new SelectList(categories.Select(c => new { c.Id, Title = paths[c.Id] }), "Id", "Title");
        var periods = await _dbContext.QuestionCategories.AsNoTracking()
            .OrderBy(c => c.TotalCategory!.OrderIndex).ThenBy(c => c.TotalCategoryId)
            .ThenBy(c => c.OrderIndex).ThenBy(c => c.Title)
            .Select(c => new { c.Id, c.Title, Course = c.TotalCategory!.Title }).ToListAsync();
        model.QuestionCategories = new SelectList(periods, "Id", "Title", model.QuestionCategoryId, "Course");
    }

    private async Task ValidateDestinations(ExtractDestinationViewModel model)
    {
        if (!await _dbContext.KnowledgeCategories.AnyAsync(c => c.Id == model.KnowledgeCategoryId))
        {
            ModelState.AddModelError(nameof(model.KnowledgeCategoryId), _localizer["Select an existing knowledge category."]);
        }
        if (!await _dbContext.QuestionCategories.AnyAsync(c => c.Id == model.QuestionCategoryId))
        {
            ModelState.AddModelError(nameof(model.QuestionCategoryId), _localizer["Select an existing period."]);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ResumeEdit(ExtractPreviewViewModel model)
    {
        var editModel = new ExtractEditViewModel
        {
             JsonContent = model.JsonContent,
             OriginalMaterial = model.OriginalMaterial,
             KnowledgeCategoryId = model.KnowledgeCategoryId,
             QuestionCategoryId = model.QuestionCategoryId
        };
        return this.StackView(editModel, "Edit");
    }
}
