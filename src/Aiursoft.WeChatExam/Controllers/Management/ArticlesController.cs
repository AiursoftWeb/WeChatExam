using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.ArticlesViewModels;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Controllers.Management;

/// <summary>
/// This controller is used to handle articles related actions like create, edit, delete, etc.
/// </summary>
[LimitPerMin]
[Authorize]
public class ArticlesController(
    WeChatExamDbContext context,
    UserManager<User> userManager) : Controller
{
    // GET: Articles
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Content Management",
        CascadedLinksIcon = "folder-tree",
        CascadedLinksOrder = 9997,
        LinkText = "Articles",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var articles = await context.Articles
            .Include(a => a.Author)
            .OrderByDescending(a => a.CreationTime)
            .ToListAsync();

        return this.StackView(new IndexViewModel
        {
            Articles = articles
        });
    }

    // GET: Articles/Create
    [Authorize(Policy = AppPermissionNames.CanAddArticles)]
    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    // POST: Articles/Create
    [Authorize(Policy = AppPermissionNames.CanAddArticles)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var article = new Article
        {
            Id = Guid.NewGuid(),
            Title = model.Title,
            Content = model.Content,
            AuthorId = user.Id
        };

        context.Articles.Add(article);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = article.Id });
    }

    // GET: Articles/Details/{id}
    public async Task<IActionResult> Details(Guid? id)
    {
        if (id == null) return NotFound();

        var article = await context.Articles
            .Include(a => a.Author)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (article == null) return NotFound();

        return this.StackView(new DetailsViewModel
        {
            Article = article
        });
    }

    // GET: Articles/Edit/{id}
    [Authorize(Policy = AppPermissionNames.CanEditArticles)]
    public async Task<IActionResult> Edit(Guid? id)
    {
        if (id == null) return NotFound();

        var article = await context.Articles.FindAsync(id);
        if (article == null) return NotFound();

        var model = new EditViewModel
        {
            Id = article.Id,
            Title = article.Title,
            Content = article.Content
        };

        return this.StackView(model);
    }

    // POST: Articles/Edit/{id}
    [Authorize(Policy = AppPermissionNames.CanEditArticles)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditViewModel model)
    {
        if (id != model.Id) return NotFound();

        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var article = await context.Articles.FindAsync(id);
        if (article == null) return NotFound();

        article.Title = model.Title;
        article.Content = model.Content;

        context.Articles.Update(article);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = article.Id });
    }

    // GET: Articles/Delete/{id}
    [Authorize(Policy = AppPermissionNames.CanDeleteArticles)]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null) return NotFound();

        var article = await context.Articles
            .Include(a => a.Author)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (article == null) return NotFound();

        return this.StackView(new DeleteViewModel
        {
            Article = article
        });
    }

    // POST: Articles/Delete/{id}
    [Authorize(Policy = AppPermissionNames.CanDeleteArticles)]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var article = await context.Articles.FindAsync(id);
        if (article == null) return NotFound();

        context.Articles.Remove(article);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
