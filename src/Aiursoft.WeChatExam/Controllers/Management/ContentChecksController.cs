using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Models.ContentChecksViewModels;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.WeChatExam.Controllers.Management;

[LimitPerMin]
[Authorize(Policy = AppPermissionNames.CanManageTags)]
public class ContentChecksController(ContentCheckService checks, IAuthorizationService authorization) : Controller
{
    [RenderInNavBar(NavGroupName = "Administration", NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management", CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 1, LinkText = "Content Configuration Checks", LinkOrder = 8)]
    [HttpGet]
    public async Task<IActionResult> Index(bool run = false, string? severity = null, string? rule = null,
        string? search = null, Guid? categoryId = null, int page = 1, Guid[]? subjects = null, bool realExam = false)
    {
        var model = new IndexViewModel
        {
            Severity = severity, Rule = rule, Search = search, CategoryId = categoryId,
            Subjects = subjects ?? [], RealExam = realExam
        };
        if (!run) return this.StackView(model);

        var report = await checks.ScanAsync(model.Subjects, realExam, HttpContext.RequestAborted);
        model.Report = report;
        var results = report.Findings.AsEnumerable();
        // Information explains intentional pricing and is not a problem counter.
        results = Enum.TryParse<CheckSeverity>(severity, out var level)
            ? results.Where(f => f.Severity == level)
            : results.Where(f => f.Severity != CheckSeverity.Information);
        if (!string.IsNullOrWhiteSpace(rule)) results = results.Where(f => f.Rule == rule);
        if (categoryId.HasValue) results = results.Where(f => f.CategoryIds.Contains(categoryId.Value));
        if (!string.IsNullOrWhiteSpace(search))
            results = results.Where(f => f.Title.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
                f.Objects.Any(o => o.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) || o.Id.Contains(search, StringComparison.OrdinalIgnoreCase)));
        var ordered = results.OrderBy(f => f.Severity).ThenBy(f => f.Rule).ThenBy(f => f.Objects.FirstOrDefault()?.Id).ToList();
        model.Total = ordered.Count;
        model.Page = Math.Clamp(page, 1, Math.Max(1, (model.Total + IndexViewModel.PageSize - 1) / IndexViewModel.PageSize));
        model.Results = ordered.Skip((model.Page - 1) * IndexViewModel.PageSize).Take(IndexViewModel.PageSize).ToList();
        foreach (var permission in model.Results.SelectMany(f => f.Objects).Concat(report.TagImpacts.Values.SelectMany(o => o)).Select(o => o.Permission).Distinct())
            if ((await authorization.AuthorizeAsync(User, permission)).Succeeded) model.EditPermissions.Add(permission);
        return this.StackView(model);
    }
}
