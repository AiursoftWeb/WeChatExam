using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ExtractViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Authorization;
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
    private readonly TemplateDbContext _dbContext;

    public ExtractController(
        IExtractService extractService,
        TemplateDbContext dbContext)
    {
        _extractService = extractService;
        _dbContext = dbContext;
    }

    public IActionResult Index(ExtractIndexViewModel? model)
    {
        model ??= new ExtractIndexViewModel();
        if (string.IsNullOrEmpty(model.SystemPrompt))
        {
            model.SystemPrompt = "You are an assistant that extracts knowledge points and questions from the provided material. " +
                           "Output a JSON array where each element contains 'KnowledgeTitle', 'KnowledgeContent', " +
                           "and a list of 'Questions'. Each question should have 'QuestionContent', 'QuestionType' " +
                           "(0=Choice, 1=Blank, 2=Bool, 3=ShortAnswer, 4=Essay), 'Metadata' (array of strings for choices, empty otherwise), " +
                           "'StandardAnswer', 'Explanation', and 'Tags' (array of strings). Do NOT wrap the JSON in Markdown. Output raw JSON only.";
        }
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extract(ExtractIndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(Index), model);
        }

        try
        {
            var json = await _extractService.GenerateJsonAsync(model.Material, model.SystemPrompt);
            var editModel = new ExtractEditViewModel
            {
                OriginalMaterial = model.Material,
                JsonContent = json
            };
            return View("Edit", editModel);
        }
        catch (Exception e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return View(nameof(Index), model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(ExtractEditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
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

            return View("Preview", previewModel);
        }
        catch (Exception e)
        {
            model.ErrorMessage = $"JSON Parse Error: {e.Message}";
            return View("Edit", model);
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
            return View("Preview", model);
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
        return View("Edit", editModel);
    }
}
