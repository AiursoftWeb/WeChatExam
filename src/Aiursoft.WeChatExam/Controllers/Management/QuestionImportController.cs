using Aiursoft.UiStack.Navigation;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.QuestionImportViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[Authorize(Policy = AppPermissionNames.CanAddQuestions)]
public class QuestionImportController : Controller
{
    private readonly WeChatExamDbContext _dbContext;
    private readonly ITagService _tagService;
    private readonly IStringLocalizer<QuestionImportController> _localizer;

    public QuestionImportController(
        WeChatExamDbContext dbContext,
        ITagService tagService,
        IStringLocalizer<QuestionImportController> localizer)
    {
        _dbContext = dbContext;
        _tagService = tagService;
        _localizer = localizer;
    }

    /// <summary>
    /// GET: QuestionImport - Show JSON input form with QuestionType dropdown
    /// </summary>
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Database Management",
        CascadedLinksIcon = "database",
        CascadedLinksOrder = 3,
        LinkText = "Import Questions",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var model = new QuestionImportIndexViewModel();
        model.QuestionCategories = await GetQuestionCategories();
        return this.StackView(model);
    }

    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Database Management",
        CascadedLinksIcon = "database",
        CascadedLinksOrder = 3,
        LinkText = "Export Questions",
        LinkOrder = 2)]
    public IActionResult Export()
    {
        return this.StackView(new QuestionExportViewModel());
    }

    private Task<List<QuestionCategory>> GetQuestionCategories() => _dbContext.QuestionCategories.AsNoTracking()
        .Include(c => c.TotalCategory)
        .OrderBy(c => c.TotalCategory!.OrderIndex).ThenBy(c => c.TotalCategoryId)
        .ThenBy(c => c.OrderIndex).ThenBy(c => c.Title).ThenBy(c => c.Id).ToListAsync();

    private List<ImportedQuestionItem> ReadQuestions(string json, QuestionType type)
    {
        if (!Enum.IsDefined(type)) throw new InvalidOperationException(_localizer["Choose a valid question type."]);
        var data = JsonConvert.DeserializeObject<List<ImportedQuestionItem?>>(json);
        if (data == null || data.Count == 0 || data.Any(q => q == null || string.IsNullOrWhiteSpace(q.Question)))
            throw new InvalidOperationException(_localizer["Provide at least one question with content."]);
        var questions = data.OfType<ImportedQuestionItem>().ToList();
        foreach (var item in questions)
        {
            item.Options ??= [];
            if (type == QuestionType.Bool) item.Options = ["正确", "错误"];
            if (type == QuestionType.Choice && item.Options.Count == 0)
                throw new InvalidOperationException(_localizer["Every choice question must have options."]);
        }
        return questions;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Preview(QuestionImportIndexViewModel model)
    {
        model.QuestionCategories = await GetQuestionCategories();
        if (!ModelState.IsValid) return this.StackView(model, nameof(Index));
        try
        {
            var data = ReadQuestions(model.JsonContent, model.SelectedQuestionType);
            return this.StackView(new QuestionImportPreviewViewModel
            {
                JsonContent = model.JsonContent, SelectedQuestionType = model.SelectedQuestionType,
                DefaultQuestionCategoryId = model.DefaultQuestionCategoryId,
                ParsedQuestions = data, QuestionCategories = model.QuestionCategories,
                QuestionCategoryIds = data.Select(q => q.QuestionCategoryId ?? model.DefaultQuestionCategoryId).ToList()
            });
        }
        catch (Exception e)
        {
            model.ErrorMessage = string.Format(_localizer["Unable to preview: {0}"].Value, e.Message);
            return this.StackView(model, nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(QuestionImportPreviewViewModel model)
    {
        model.QuestionCategories = await GetQuestionCategories();
        try
        {
            // Re-read the submitted JSON and validate the whole batch before any tag service
            // call: creating a source tag can save tracked questions as a side effect.
            model.ParsedQuestions = ReadQuestions(model.JsonContent, model.SelectedQuestionType);
            var validIds = model.QuestionCategories.Select(c => c.Id).ToHashSet();
            if (!ModelState.IsValid || model.QuestionCategoryIds.Count != model.ParsedQuestions.Count ||
                model.QuestionCategoryIds.Any(id => !id.HasValue || !validIds.Contains(id.Value)))
            {
                model.ErrorMessage = _localizer["Choose an existing period for every question. Nothing has been imported."];
                return ImportPreview(model);
            }

            var gradingStrategy = model.SelectedQuestionType switch
            {
                QuestionType.Choice or QuestionType.Bool => GradingStrategy.ExactMatch,
                QuestionType.Blank => GradingStrategy.FuzzyMatch,
                _ => GradingStrategy.AiEval
            };
            var ids = model.ParsedQuestions.Select(_ => Guid.NewGuid()).ToArray();
            await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                // The entire batch commits together. A lost commit acknowledgment must not
                // create another batch when the database execution strategy retries.
                if (await _dbContext.Questions.AnyAsync(q => q.Id == ids[0])) return;
                await using var transaction = await _dbContext.Database.BeginTransactionAsync();
                for (var i = 0; i < model.ParsedQuestions.Count; i++)
                {
                    var item = model.ParsedQuestions[i];
                    var question = new Question
                    {
                        Id = ids[i], Content = item.Question, QuestionType = model.SelectedQuestionType,
                        GradingStrategy = gradingStrategy,
                        Metadata = model.SelectedQuestionType is QuestionType.Choice or QuestionType.Bool
                            ? JsonConvert.SerializeObject(new { options = item.Options }) : string.Empty,
                        StandardAnswer = item.Answer, Explanation = item.Analysis,
                        QuestionCategoryId = model.QuestionCategoryIds[i]!.Value
                    };
                    _dbContext.Questions.Add(question);
                    if (!string.IsNullOrWhiteSpace(item.OriginalFilename))
                    {
                        var tag = await _tagService.AddTagAsync(item.OriginalFilename.Trim());
                        _dbContext.QuestionTags.Add(new QuestionTag { QuestionId = question.Id, TagId = tag.Id });
                    }
                }
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            });
            TempData["SuccessMessage"] = string.Format(_localizer["Successfully imported {0} questions."].Value, ids.Length);
            return RedirectToAction(nameof(Index));
        }
        catch (Exception e)
        {
            _dbContext.ChangeTracker.Clear();
            model.ErrorMessage = string.Format(_localizer["Unable to import: {0}"].Value, e.Message);
            return ImportPreview(model);
        }
    }

    private IActionResult ImportPreview(QuestionImportPreviewViewModel model)
    {
        // A forged/incomplete form can omit rows. Keep the correction page usable without
        // treating the missing selections as valid input for the failed save.
        while (model.QuestionCategoryIds.Count < model.ParsedQuestions.Count) model.QuestionCategoryIds.Add(null);
        return this.StackView(model, "Preview");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResumeEdit(QuestionImportPreviewViewModel model)
    {
        // Keep per-question choices in the JSON so editing or reordering the text cannot
        // accidentally attach an old row's period to a different question. Preserve extra fields.
        try
        {
            var data = JArray.Parse(model.JsonContent);
            for (var i = 0; i < data.Count; i++)
            {
                if (data[i] is JObject item)
                {
                    foreach (var property in item.Properties().Where(p => p.Name.Equals("QuestionCategoryId", StringComparison.OrdinalIgnoreCase)).ToList())
                        property.Remove();
                    item["QuestionCategoryId"] = model.QuestionCategoryIds.ElementAtOrDefault(i)?.ToString();
                }
            }
            model.JsonContent = data.ToString(Formatting.Indented);
        }
        catch (JsonException)
        {
            // Malformed input can still be taken back to the editor for correction.
        }
        ModelState.Clear();
        return this.StackView(new QuestionImportIndexViewModel
        {
            JsonContent = model.JsonContent, SelectedQuestionType = model.SelectedQuestionType,
            DefaultQuestionCategoryId = model.DefaultQuestionCategoryId,
            QuestionCategories = await GetQuestionCategories()
        }, nameof(Index));
    }
}
