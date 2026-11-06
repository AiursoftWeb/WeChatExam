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
    public async Task EmptyContentActionsKeepTheCourseAndDirectoryWhenCreating()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var emptyCourse = new TotalCategory { Title = "Empty course" };
        var otherCourse = new TotalCategory { Title = "Other course" };
        var directory = new KnowledgeCategory { Title = "Parent directory", TotalCategoryId = otherCourse.Id, TotalCategory = otherCourse };
        db.TotalCategories.AddRange(emptyCourse, otherCourse);
        db.KnowledgeCategories.Add(directory);
        await db.SaveChangesAsync();

        var coursePage = await Http.GetStringAsync($"/TotalCategories/Details/{emptyCourse.Id}");
        Assert.Contains($"/QuestionCategories/Create?totalCategoryId={emptyCourse.Id}", coursePage);
        Assert.Contains($"/KnowledgeCategories/Create?totalCategoryId={emptyCourse.Id}", coursePage);
        Assert.Contains($"/MockPapers/Create?totalCategoryId={emptyCourse.Id}", coursePage);
        var periodForm = await Http.GetStringAsync($"/QuestionCategories/Create?totalCategoryId={emptyCourse.Id}");
        var directoryForm = await Http.GetStringAsync($"/KnowledgeCategories/Create?totalCategoryId={emptyCourse.Id}");
        foreach (var form in new[] { periodForm, directoryForm })
            Assert.Contains(emptyCourse.Id.ToString(), form);

        var directoryPage = await Http.GetStringAsync($"/KnowledgeCategories/Details/{directory.Id}");
        Assert.Contains($"parentKnowledgeCategoryId={directory.Id}", directoryPage);
        Assert.Contains($"/KnowledgePoints/Create?knowledgeCategoryId={directory.Id}", directoryPage);
        var childForm = await Http.GetStringAsync($"/KnowledgeCategories/Create?totalCategoryId={otherCourse.Id}&parentKnowledgeCategoryId={directory.Id}");
        Assert.IsTrue(Regex.IsMatch(childForm, $"<option[^>]*value=\"{directory.Id}\"[^>]*selected|<option[^>]*selected[^>]*value=\"{directory.Id}\""));
        var pointForm = await Http.GetStringAsync($"/KnowledgePoints/Create?knowledgeCategoryId={directory.Id}");
        Assert.IsTrue(Regex.IsMatch(pointForm, $"<input[^>]*value=\"{directory.Id}\"[^>]*checked|<input[^>]*checked[^>]*value=\"{directory.Id}\""));
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/KnowledgeCategories/Create?totalCategoryId={emptyCourse.Id}&parentKnowledgeCategoryId={directory.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/KnowledgePoints/Create?knowledgeCategoryId={Guid.NewGuid()}")).StatusCode);
    }

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

        var html = await Http.GetStringAsync($"/TotalCategories/Edit/{course.Id}");
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
        Assert.Contains($"/TotalCategories/Details/{course.Id}", await Http.GetStringAsync("/TotalCategories"));
        Assert.Contains("Scoped period", await Http.GetStringAsync($"/TotalCategories/Details/{course.Id}"));
        var emptyHtml = await Http.GetStringAsync($"/TotalCategories/Edit/{empty.Id}");
        var emptyContents = Regex.Match(emptyHtml, $"data-course-contents=\"{empty.Id}\"[^>]*>(.*?)</div>", RegexOptions.Singleline).Groups[1].Value;
        Assert.AreEqual(5, Regex.Matches(emptyContents, ">0</span>").Count);
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
        Assert.Contains("Ask an administrator with VIP product permissions", html);
        Assert.DoesNotContain($"/VipProducts/Create?totalCategoryId={course.Id}", html);
    }

    [TestMethod]
    public async Task CourseKnowledgePointsAreUniqueAndDirectoryFiltersKeepTheirScope()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Duplicate course" };
        var other = new TotalCategory { Title = course.Title };
        var root = new KnowledgeCategory { Title = "Root directory", TotalCategoryId = course.Id, TotalCategory = course };
        var child = new KnowledgeCategory { Title = "Child directory", TotalCategoryId = course.Id, TotalCategory = course, ParentKnowledgeCategory = root };
        var unrelated = new KnowledgeCategory { Title = root.Title, TotalCategoryId = other.Id, TotalCategory = other };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Shared point", Content = "Shared content" };
        var otherPoint = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Other course point", Content = "Other content" };
        db.KnowledgeCategoryKnowledgePoints.AddRange(
            new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgeCategoryId = root.Id, KnowledgePointId = point.Id, KnowledgeCategory = root, KnowledgePoint = point },
            new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgeCategoryId = child.Id, KnowledgePointId = point.Id, KnowledgeCategory = child, KnowledgePoint = point },
            new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgeCategoryId = unrelated.Id, KnowledgePointId = otherPoint.Id, KnowledgeCategory = unrelated, KnowledgePoint = otherPoint });
        await db.SaveChangesAsync();
        var editor = await Http.GetStringAsync($"/TotalCategories/Edit/{course.Id}");
        var link = Regex.Match(editor, $"<a[^>]*href=\"/KnowledgePoints\\?totalCategoryId={course.Id}\"[^>]*>(.*?)</a>", RegexOptions.Singleline);
        Assert.IsTrue(link.Success);
        Assert.Contains(">1</span>", link.Groups[1].Value);
        foreach (var path in new[] { $"/KnowledgePoints?totalCategoryId={course.Id}", $"/KnowledgePoints?knowledgeCategoryId={child.Id}", $"/KnowledgePoints?totalCategoryId={course.Id}&knowledgeCategoryId={root.Id}" })
        {
            var html = await Http.GetStringAsync(path);
            Assert.AreEqual(1, Regex.Matches(html, ">Shared point</td>").Count);
            Assert.DoesNotContain("Other course point", html);
            Assert.Contains($"/TotalCategories/Details/{course.Id}", html);
        }
        Assert.Contains($"/KnowledgePoints?totalCategoryId={course.Id}", await Http.GetStringAsync($"/TotalCategories/Details/{course.Id}"));
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/KnowledgePoints?totalCategoryId={Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/KnowledgePoints?knowledgeCategoryId={Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/KnowledgePoints?totalCategoryId={other.Id}&knowledgeCategoryId={root.Id}")).StatusCode);
    }

    [TestMethod]
    public async Task CourseSalesUseSavedScopeAndPreselectTheExactCourse()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Sales course" };
        var other = new TotalCategory { Title = course.Title };
        db.TotalCategories.AddRange(course, other);
        await db.SaveChangesAsync();
        var path = $"/TotalCategories/Edit/{course.Id}";
        var empty = await Http.GetStringAsync(path);
        Assert.Contains("Students cannot buy this course.", empty);
        Assert.Contains($"/VipProducts/Create?totalCategoryId={course.Id}", empty);
        var create = await Http.GetStringAsync($"/VipProducts/Create?totalCategoryId={course.Id}");
        Assert.IsTrue(Regex.IsMatch(create, $"<option[^>]*selected[^>]*value=\"{course.Id}\""));
        var product = new VipProduct { Name = "Exact course plan", Type = VipProductType.Category, TotalCategoryId = course.Id, PriceInFen = 1234, DurationDays = 30, IsEnabled = false };
        db.VipProducts.AddRange(product, new VipProduct { Name = "Different course plan", Type = VipProductType.Category, TotalCategoryId = other.Id, PriceInFen = 100, DurationDays = 7 });
        await db.SaveChangesAsync();
        var paused = await Http.GetStringAsync(path);
        Assert.Contains("Sales paused", paused);
        Assert.Contains("12.34", paused);
        Assert.Contains($"/VipProducts/Edit/{product.Id}", paused);
        Assert.DoesNotContain("Different course plan", paused);
        product.IsEnabled = true;
        db.VipProducts.Add(new VipProduct { Name = "Second course plan", Type = VipProductType.Category, TotalCategoryId = course.Id, PriceInFen = 100, DurationDays = 7, IsEnabled = false });
        await db.SaveChangesAsync();
        Assert.Contains("On sale", await Http.GetStringAsync(path));
        Assert.Contains($"/VipProducts?totalCategoryId={course.Id}", await Http.GetStringAsync(path));
        var filtered = await Http.GetStringAsync($"/VipProducts?totalCategoryId={course.Id}&search=course");
        Assert.Contains("Exact course plan", filtered);
        Assert.DoesNotContain("Second course plan", filtered);
        var disabled = await Http.GetStringAsync($"/VipProducts?totalCategoryId={course.Id}&search=course&showDisabled=true");
        Assert.Contains("Second course plan", disabled);
        Assert.DoesNotContain("Exact course plan", disabled);
        Assert.DoesNotContain("Different course plan", disabled);
        Assert.DoesNotContain("Different course plan", filtered);
        Assert.Contains($"name=\"totalCategoryId\" value=\"{course.Id}\"", filtered);
        var invalid = await PostForm(path, new Dictionary<string, string> { ["Id"] = course.Id.ToString(), ["Title"] = "", ["OrderIndex"] = "-1" });
        Assert.Contains($"data-course-sales=\"{course.Id}\"", await invalid.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/VipProducts/Create?totalCategoryId={Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/VipProducts?totalCategoryId={course.Id}&institutionId={Guid.NewGuid()}")).StatusCode);
        Assert.DoesNotContain("data-course-sales", await Http.GetStringAsync("/TotalCategories/Create"));
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
