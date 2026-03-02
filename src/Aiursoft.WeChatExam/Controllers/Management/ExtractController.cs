using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Services.BackgroundJobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanUseAIExtractor)]
public class ExtractController : Controller
{
    private readonly IExtractService _extractService;
    private readonly WeChatExamDbContext _dbContext;
    private readonly BackgroundJobQueue _backgroundJobQueue;
    private readonly GlobalSettingsService _globalSettingsService;

    public ExtractController(
        IExtractService extractService,
        WeChatExamDbContext dbContext,
        BackgroundJobQueue backgroundJobQueue,
        GlobalSettingsService globalSettingsService)
    {
        _extractService = extractService;
        _dbContext = dbContext;
        _backgroundJobQueue = backgroundJobQueue;
        _globalSettingsService = globalSettingsService;
    }

    public async Task<IActionResult> Index(ExtractIndexViewModel? model)
    {
        model ??= new ExtractIndexViewModel();
        if (string.IsNullOrEmpty(model.SystemPrompt))
        {
            model.SystemPrompt = await _globalSettingsService.GetSettingValueAsync(SettingsMap.AiPromptExtractDefault);
        }

        var categories = await _dbContext.Categories
            .OrderBy(c => c.Title)
            .ToListAsync();
        model.Categories = new SelectList(categories, nameof(Category.Id), nameof(Category.Title));
        
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extract(ExtractIndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            var categories = await _dbContext.Categories
                .OrderBy(c => c.Title)
                .ToListAsync();
            model.Categories = new SelectList(categories, nameof(Category.Id), nameof(Category.Title));
            return this.StackView(model, nameof(Index));
        }

        _backgroundJobQueue.QueueWithDependency<IExtractService>(
            queueName: "Extraction",
            jobName: $"AI Extraction to Category {model.CategoryId}",
            job: async (extractService) =>
            {
                var json = await extractService.GenerateJsonAsync(model.Material, model.SystemPrompt);
                var data = JsonConvert.DeserializeObject<List<ExtractedKnowledgePoint>>(json);
                if (data != null)
                {
                    await extractService.SaveAsync(data, model.CategoryId);
                }
                return json;
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

            var categories = await _dbContext.Categories
                .OrderBy(c => c.Title)
                .ToListAsync();

            var previewModel = new ExtractPreviewViewModel
            {
                JsonContent = model.JsonContent,
                Data = data,
                OriginalMaterial = model.OriginalMaterial,
                Categories = new SelectList(categories, nameof(Category.Id), nameof(Category.Title))
            };

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

            await _extractService.SaveAsync(data, model.CategoryId);
            
            return RedirectToAction(nameof(Index)); // Or a success page
        }
        catch (Exception e)
        {
            // If error, reload preview with error.
            // We need to reload Categories
            var categories = await _dbContext.Categories
                .OrderBy(c => c.Title)
                .ToListAsync();

            model.Categories = new SelectList(categories, nameof(Category.Id), nameof(Category.Title));
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ResumeEdit(ExtractPreviewViewModel model)
    {
        var editModel = new ExtractEditViewModel
        {
             JsonContent = model.JsonContent,
             OriginalMaterial = model.OriginalMaterial
        };
        return this.StackView(editModel, "Edit");
    }
}
