using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.ContentChecksViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanManageTags)]
public class ContentChecksController(ContentCheckService checks, IAuthorizationService authorization,
    IStringLocalizer<ContentChecksController> localizer) : Controller
{
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management", CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1, LinkText = "Content Configuration Checks", LinkOrder = 8)]
    [HttpGet]
    public IActionResult Index(bool run = false)
    {
        // Keep existing report bookmarks usable after splitting the page.
        if (run) return Redirect(Url.Action(nameof(Results)) + Request.QueryString);
        return this.StackView(new IndexViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> Results(string view = "all", string? severity = null, string? rule = null,
        string? search = null, Guid? categoryId = null, int page = 1, Guid[]? subjects = null,
        bool realExam = false, bool changed = false, Guid[]? institutions = null)
    {
        var model = await ScanAsync(view, severity, rule, search, categoryId, page, subjects, realExam, institutions);
        model.Changed = changed;
        var results = model.Groups.Where(g => view == "engineering" ? g.NeedsEngineering : !g.NeedsEngineering);
        if (Enum.TryParse<CheckSeverity>(severity, out var level)) results = results.Where(g => g.Severity == level);
        if (view == "attention") results = results.Where(g => g.Severity != CheckSeverity.Information);
        if (view == "information") results = results.Where(g => g.Severity == CheckSeverity.Information);
        if (!string.IsNullOrWhiteSpace(rule)) results = results.Where(g => g.Rule == rule);
        if (categoryId.HasValue) results = results.Where(g => g.CategoryIds.Contains(categoryId.Value));
        if (!string.IsNullOrWhiteSpace(search))
            results = results.Where(g => g.Caption.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                g.Objects.Any(o => o.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || o.Id.Contains(search, StringComparison.OrdinalIgnoreCase)));
        var ordered = results.OrderBy(g => g.Severity).ThenBy(g => g.Rule).ThenBy(g => g.Key).ToList();
        model.Total = ordered.Count;
        model.Page = Math.Clamp(page, 1, Math.Max(1, (model.Total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        model.Results = ordered.Skip((model.Page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToList();
        model.ResultsUrl = ReportUrl(model);
        model.PreviousUrl = model.Page > 1 ? ReportUrl(model, model.Page - 1) : null;
        model.NextUrl = model.Page * IndexViewModel.PageSize < model.Total ? ReportUrl(model, model.Page + 1) : null;
        foreach (var filter in new[] { "all", "attention", "information", "engineering" })
            model.ViewUrls[filter] = ReportUrl(model, 1, filter);
        foreach (var group in model.Results) group.DetailsUrl = DetailUrl(model, group.Key);
        return this.StackView(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(string key, string view = "all", string? severity = null, string? rule = null,
        string? search = null, Guid? categoryId = null, int page = 1, Guid[]? subjects = null, bool realExam = false,
        Guid? exerciseId = null, Guid[]? institutions = null)
    {
        var model = await ScanAsync(view, severity, rule, search, categoryId, page, subjects, realExam, institutions);
        model.Selected = model.Groups.FirstOrDefault(g => g.Key == key);
        model.ResultsUrl = ReportUrl(model);
        // Each request reads live data. A corrected or deleted finding must not lead to
        // a broken detail page, nor be replaced by a different item at the same list index.
        if (model.Selected == null) return Redirect(model.ResultsUrl + "&changed=true");
        model.Exercises = model.Selected.Objects.Where(o => o.Controller == "Questions")
            .Select(o => model.Report!.Exercises.GetValueOrDefault(Guid.Parse(o.Id))).OfType<ContentExercise>()
            .OrderBy(e => e.Question.Id, StringComparer.Ordinal).ToList();
        model.ExerciseIndex = Math.Max(0, model.Exercises.FindIndex(e => e.Question.Id == exerciseId?.ToString()));
        model.Exercise = model.Exercises.ElementAtOrDefault(model.ExerciseIndex);
        model.DetailsUrl = DetailUrl(model, key, model.Exercise?.Question.Id);
        foreach (var exercise in model.Exercises) model.ExerciseUrls[exercise.Question.Id] = DetailUrl(model, key, exercise.Question.Id);
        if (model.ExerciseIndex > 0) model.PreviousExerciseUrl = DetailUrl(model, key, model.Exercises[model.ExerciseIndex - 1].Question.Id);
        if (model.ExerciseIndex + 1 < model.Exercises.Count) model.NextExerciseUrl = DetailUrl(model, key, model.Exercises[model.ExerciseIndex + 1].Question.Id);
        foreach (var permission in model.Selected.Objects.Concat(model.Report!.TagImpacts.Values.SelectMany(o => o))
                     .Concat(model.Exercises.SelectMany(e => e.Settings)).Select(o => o.Permission).Distinct())
            if ((await authorization.AuthorizeAsync(User, permission)).Succeeded) model.EditPermissions.Add(permission);
        return this.StackView(model);
    }

    private async Task<IndexViewModel> ScanAsync(string view, string? severity, string? rule, string? search,
        Guid? categoryId, int page, Guid[]? subjects, bool realExam, Guid[]? institutions)
    {
        var report = await checks.ScanAsync(subjects ?? [], realExam, HttpContext.RequestAborted, institutions);
        var groups = ContentFindingGroup.Build(report.Findings);
        foreach (var group in groups)
        {
            group.Caption = Caption(group.Rule);
            group.StudentExperience = StudentExperience(group.Rule);
        }
        return new IndexViewModel
        {
            Report = report, Groups = groups, View = view, Severity = severity, Rule = rule,
            Search = search, CategoryId = categoryId, Page = page, Subjects = subjects ?? [], RealExam = realExam, Institutions = institutions ?? []
        };
    }

    private string Caption(string rule) => rule switch
    {
        "KP001" => localizer["Free lesson has a paid exercise"],
        "KP003" => localizer["This lesson needs a directory"],
        "KP002" => localizer["The lesson and exercise belong to different courses"],
        "VIP002" or "VIP004" => localizer["No suitable VIP is currently available to buy"],
        "VIP003" => localizer["This VIP product needs a course"],
        "VIP005" => localizer["This VIP product needs an institution"],
        "PAPER001" => localizer["This paper is missing its published version"],
        "PAPER002" => localizer["This paper version has no exercises"],
        "PAPER005" => localizer["This legacy paper needs migration"],
        "PAPER004" => localizer["Free paper includes paid exercises"],
        _ => localizer["Mini Program entry checks need review"]
    };

    private string StudentExperience(string rule) => rule switch
    {
        "KP001" => localizer["Visitors can read the lesson in a free directory, but its exercise requires a course VIP. Choose a free exercise or review the directory and period settings."],
        "KP003" => localizer["Students cannot open this lesson until it is assigned to a knowledge directory."],
        "KP002" => localizer["The knowledge point is linked to one course, while these exercises belong to another. Buying the first course's VIP may not unlock the exercises."],
        "VIP002" or "VIP004" => localizer["Existing students with a valid VIP may still have access. New students cannot currently buy a suitable VIP to unlock this content."],
        "VIP003" => localizer["This VIP product is missing its course. It cannot grant the intended course access."],
        "VIP005" => localizer["This VIP product is missing its institution. It cannot grant the intended real-paper access."],
        "PAPER001" => localizer["Students cannot find this paper because a published paper version has not been created."],
        "PAPER002" => localizer["This saved paper version contains no exercises, so it cannot provide a usable practice paper."],
        "PAPER005" => localizer["This paper is kept for historical exams. Confirm its course or institution with the development team before making it available in the new paper lists."],
        "PAPER004" => localizer["Students can open this free paper even when its original exercises are paid. Paper pricing and individual exercise pricing are independent."],
        _ => localizer["Different screens apply different access checks. Our team needs to review the Mini Program behavior."]
    };

    private string ReportUrl(IndexViewModel model, int? page = null, string? view = null) =>
        Url.Action(nameof(Results)) + FilterQuery(model, page, view);

    private string DetailUrl(IndexViewModel model, string key, string? exerciseId = null) =>
        Url.Action(nameof(Details)) + FilterQuery(model) + "&key=" + Uri.EscapeDataString(key)
        + (exerciseId == null ? string.Empty : "&exerciseId=" + Uri.EscapeDataString(exerciseId));

    private static QueryString FilterQuery(IndexViewModel model, int? page = null, string? view = null)
    {
        var values = new List<KeyValuePair<string, string?>>
        {
            new("view", view ?? model.View), new("page", (page ?? model.Page).ToString()),
            new("severity", model.Severity), new("rule", model.Rule), new("search", model.Search),
            new("categoryId", model.CategoryId?.ToString()), new("realExam", model.RealExam.ToString())
        };
        values.AddRange(model.Subjects.Select(id => new KeyValuePair<string, string?>("subjects", id.ToString())));
        values.AddRange(model.Institutions.Select(id => new KeyValuePair<string, string?>("institutions", id.ToString())));
        return QueryString.Create(values.Where(v => !string.IsNullOrEmpty(v.Value)));
    }
}
