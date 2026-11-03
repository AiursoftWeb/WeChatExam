using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TotalCategoriesTests : TestBase
{
    [TestMethod]
    public async Task CourseContentCountsAndLinksKeepSameNamedCoursesSeparate()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Same course name" };
        var empty = new TotalCategory { Title = course.Title };
        var period = new QuestionCategory { Title = "Scoped period", TotalCategory = course, TotalCategoryId = course.Id };
        db.TotalCategories.Add(empty);
        db.QuestionCategories.Add(period);
        db.Questions.AddRange(Enumerable.Range(1, 21).Select(i => new Question
        {
            Id = Guid.NewGuid(), Content = $"Scoped question {i}", QuestionCategory = period
        }));
        db.KnowledgeCategories.Add(new KnowledgeCategory { Title = "Scoped knowledge category", TotalCategoryId = course.Id });
        db.MockPapers.Add(new MockPaper { Title = "Scoped mock", TotalCategoryId = course.Id });
        await db.SaveChangesAsync();

        foreach (var path in new[] { "/TotalCategories", $"/TotalCategories/Edit/{course.Id}" })
        {
            var html = await Http.GetStringAsync(path);
            var contents = Regex.Match(html, $"data-course-contents=\"{course.Id}\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
            Assert.IsFalse(string.IsNullOrEmpty(contents));
            foreach (var (controller, count) in new[] { ("QuestionCategories", 1), ("Questions", 21), ("KnowledgeCategories", 1), ("MockPapers", 1) })
            {
                var link = Regex.Match(contents, $"<a[^>]*href=\"/{controller}\\?totalCategoryId={course.Id}\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
                Assert.IsTrue(link.Success, controller);
                Assert.Contains($">{count}</span>", link.Groups[1].Value);
                var target = await Http.GetStringAsync($"/{controller}?totalCategoryId={course.Id}");
                Assert.Contains(controller == "Questions" ? "Scoped question" : controller == "QuestionCategories" ? "Scoped period" : controller == "KnowledgeCategories" ? "Scoped knowledge category" : "Scoped mock", target);
            }
        }
        var emptyHtml = await Http.GetStringAsync($"/TotalCategories/Edit/{empty.Id}");
        var emptyContents = Regex.Match(emptyHtml, $"data-course-contents=\"{empty.Id}\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.AreEqual(4, Regex.Matches(emptyContents, ">0</span>").Count);
        var nextPage = await Http.GetStringAsync($"/Questions?totalCategoryId={course.Id}&page=2");
        Assert.Contains("Scoped question", nextPage);
        Assert.Contains(course.Id.ToString(), nextPage);

        var invalid = await PostForm($"/TotalCategories/Edit/{course.Id}", new Dictionary<string, string>
        {
            ["Id"] = course.Id.ToString(), ["Title"] = "", ["OrderIndex"] = "-1"
        });
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains($"data-course-contents=\"{course.Id}\"", await invalid.Content.ReadAsStringAsync());
        Assert.DoesNotContain("data-course-contents", await Http.GetStringAsync("/TotalCategories/Create"));
    }

    [TestMethod]
    public async Task CategoryEditorsWithoutQuestionOrPaperPermissionsOnlySeeAccessibleContentLinks()
    {
        var (email, password) = await RegisterAndLoginAsync();
        using var scope = Server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = (await manager.FindByEmailAsync(email))!;
        Assert.IsTrue((await manager.AddClaimAsync(user, new System.Security.Claims.Claim(AppPermissions.Type, AppPermissionNames.CanEditAnyCategory))).Succeeded);
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Editable course" };
        db.TotalCategories.Add(course);
        await db.SaveChangesAsync();
        var html = await Http.GetStringAsync($"/TotalCategories/Edit/{course.Id}");
        Assert.Contains($"/QuestionCategories?totalCategoryId={course.Id}", html);
        Assert.Contains($"/KnowledgeCategories?totalCategoryId={course.Id}", html);
        Assert.DoesNotContain($"/Questions?totalCategoryId={course.Id}", html);
        Assert.DoesNotContain($"/MockPapers?totalCategoryId={course.Id}", html);
    }

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        // The existing HTTP fixture shares its in-memory store across hosts.
        db.TotalCategories.RemoveRange(await db.TotalCategories.ToListAsync());
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task AdministratorsCanCreateEditAndOrderIndependentTotalCategories()
    {
        await LoginAsAdmin();
        int legacyCount;
        using (var scope = Server!.Services.CreateScope())
        {
            legacyCount = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Categories.CountAsync();
        }
        var response = await PostForm("/TotalCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "  Music history  ",
            ["OrderIndex"] = "10"
        });
        AssertRedirect(response, "/TotalCategories");

        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var category = await db.TotalCategories.SingleAsync();
            Assert.AreEqual("Music history", category.Title);
            Assert.AreEqual(10, category.OrderIndex);
            Assert.AreEqual(legacyCount, await db.Categories.CountAsync());
            id = category.Id;
        }

        response = await PostForm($"/TotalCategories/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Title"] = "Chinese music history",
            ["OrderIndex"] = "1"
        });
        AssertRedirect(response, "/TotalCategories");
        await PostForm("/TotalCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "Western music history",
            ["OrderIndex"] = "2"
        });
        var index = await Http.GetStringAsync("/TotalCategories");
        Assert.Contains($"/TotalCategories/Edit/{id}", index);
        Assert.IsTrue(index.IndexOf("Chinese music history", StringComparison.Ordinal) <
                      index.IndexOf("Western music history", StringComparison.Ordinal));
        using var verifyScope = Server!.Services.CreateScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
            .TotalCategories.SingleAsync(c => c.Id == id);
        Assert.AreEqual("Chinese music history", stored.Title);
        Assert.AreEqual(1, stored.OrderIndex);
    }

    [TestMethod]
    public async Task InvalidInputDoesNotCreateOrOverwriteACategory()
    {
        await LoginAsAdmin();
        foreach (var title in new[] { "   ", new string('x', 201) })
        {
            var invalid = await PostForm("/TotalCategories/Create", new Dictionary<string, string>
            {
                ["Title"] = title,
                ["OrderIndex"] = "0"
            });
            Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        }
        var invalidOrder = await PostForm("/TotalCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "Music history",
            ["OrderIndex"] = "-1"
        });
        Assert.AreEqual(HttpStatusCode.OK, invalidOrder.StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
                .TotalCategories.CountAsync());
        }

        await PostForm("/TotalCategories/Create", new Dictionary<string, string> { ["Title"] = "Music history" });
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
                .TotalCategories.Select(c => c.Id).SingleAsync();
        }
        var edit = await PostForm($"/TotalCategories/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(),
            ["Title"] = "",
            ["OrderIndex"] = "5"
        });
        Assert.AreEqual(HttpStatusCode.OK, edit.StatusCode);
        var editHtml = await edit.Content.ReadAsStringAsync();
        Assert.Contains(id.ToString(), editHtml);
        using var verifyScope = Server!.Services.CreateScope();
        var saved = await verifyScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>()
            .TotalCategories.SingleAsync();
        Assert.AreEqual("Music history", saved.Title);
        Assert.AreEqual(0, saved.OrderIndex);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/TotalCategories/Edit/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task ManagementRequiresCategoryPermissionAndValidAntiforgeryToken()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/TotalCategories")).StatusCode);
        await RegisterAndLoginAsync();
        foreach (var url in new[] { "/TotalCategories", "/TotalCategories/Create", $"/TotalCategories/Edit/{Guid.NewGuid()}" })
        {
            var response = await Http.GetAsync(url);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        }
        var deniedPost = await PostForm("/TotalCategories/Create",
            new Dictionary<string, string> { ["Title"] = "Not permitted" }, includeToken: false);
        Assert.IsTrue(deniedPost.StatusCode != HttpStatusCode.OK);
        await LoginAsAdmin();
        var noToken = await PostForm("/TotalCategories/Create",
            new Dictionary<string, string> { ["Title"] = "No token" }, includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().TotalCategories.CountAsync());
    }

    [TestMethod]
    public async Task MigrationCopiesOnlyRootCategoriesWithoutChangingProducts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddTotalCategories", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, index);
        await migrator.MigrateAsync(migrations[index - 1]);
        var root = new Category { Id = Guid.NewGuid(), Title = "Music history", ParentId = null, OrderIndex = 7 };
        var child = new Category { Id = Guid.NewGuid(), Title = "Overview", ParentId = root.Id };
        var product = new VipProduct { Name = "Music membership", TotalCategoryId = root.Id, DurationDays = 366, PriceInFen = 19800 };
        db.Categories.AddRange(root, child);
        await db.SaveChangesAsync();
        // Seed the historical column, not the current entity model, before upgrading.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO VipProducts (Id, Name, Type, CategoryId, PriceInFen, DurationDays, IsEnabled, CreationTime)
            VALUES ({product.Id}, {product.Name}, {0}, {root.Id}, {product.PriceInFen}, {product.DurationDays}, {true}, {product.CreationTime})
            """);
        await migrator.MigrateAsync();

        var category = await db.TotalCategories.SingleAsync();
        Assert.AreEqual(root.Id, category.Id);
        Assert.AreEqual(root.Title, category.Title);
        Assert.AreEqual(root.OrderIndex, category.OrderIndex);
        Assert.AreEqual(root.CreationTime, category.CreationTime);
        Assert.AreEqual(2, await db.Categories.CountAsync());
        db.ChangeTracker.Clear();
        var savedProduct = await db.VipProducts.SingleAsync();
        Assert.AreEqual(product.Id, savedProduct.Id);
        Assert.AreEqual(root.Id, savedProduct.TotalCategoryId);
        Assert.AreEqual(366, savedProduct.DurationDays);
        Assert.AreEqual(19800, savedProduct.PriceInFen);
        await migrator.MigrateAsync();
        Assert.AreEqual(1, await db.TotalCategories.CountAsync());
    }
}
