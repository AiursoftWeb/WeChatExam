using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionCategoriesTests : TestBase
{
    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.QuestionCategories.RemoveRange(await db.QuestionCategories.ToListAsync());
        db.TotalCategories.RemoveRange(await db.TotalCategories.ToListAsync());
        await db.SaveChangesAsync();
    }

    private async Task<(Guid First, Guid Second)> CreateCourses()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var first = new TotalCategory { Title = "Chinese music history", OrderIndex = 0 };
        var second = new TotalCategory { Title = "Western music history", OrderIndex = 1 };
        db.TotalCategories.AddRange(first, second);
        await db.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    [TestMethod]
    public async Task CreateEditAndCourseTabsKeepPeriodsInExactlyOneCourse()
    {
        await LoginAsAdmin();
        var (first, second) = await CreateCourses();
        var response = await PostForm("/QuestionCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "  Early music  ", ["TotalCategoryId"] = first.ToString(),
            ["IsFree"] = "true", ["OrderIndex"] = "4"
        });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(first.ToString(), response.Headers.Location!.ToString());
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var category = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().QuestionCategories.SingleAsync();
            id = category.Id;
            Assert.AreEqual("Early music", category.Title);
            Assert.AreEqual(first, category.TotalCategoryId);
            Assert.IsTrue(category.IsFree);
        }
        var index = await Http.GetStringAsync("/QuestionCategories");
        Assert.Contains($"/QuestionCategories/Edit/{id}", index);
        var other = await Http.GetStringAsync($"/QuestionCategories?totalCategoryId={second}");
        Assert.DoesNotContain($"/QuestionCategories/Edit/{id}", other);

        response = await PostForm($"/QuestionCategories/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Title"] = "Medieval music", ["TotalCategoryId"] = second.ToString(),
            ["IsFree"] = "false", ["OrderIndex"] = "2"
        });
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(second.ToString(), response.Headers.Location!.ToString());
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().QuestionCategories.SingleAsync();
        Assert.AreEqual(id, saved.Id);
        Assert.AreEqual(second, saved.TotalCategoryId);
        Assert.AreEqual("Medieval music", saved.Title);
        Assert.AreEqual(2, saved.OrderIndex);
        Assert.IsFalse(saved.IsFree);
        index = await Http.GetStringAsync($"/QuestionCategories?totalCategoryId={first}");
        Assert.DoesNotContain($"/QuestionCategories/Edit/{id}", index);
        other = await Http.GetStringAsync($"/QuestionCategories?totalCategoryId={second}");
        Assert.Contains($"/QuestionCategories/Edit/{id}", other);
    }

    [TestMethod]
    public async Task InvalidInputPreservesTheFormAndNeverChangesStoredPeriods()
    {
        await LoginAsAdmin();
        var (first, second) = await CreateCourses();
        foreach (var total in new[] { "", Guid.Empty.ToString(), Guid.NewGuid().ToString() })
        {
            var response = await PostForm("/QuestionCategories/Create", new Dictionary<string, string>
            {
                ["Title"] = "Keep this title", ["TotalCategoryId"] = total, ["IsFree"] = "true"
            });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Keep this title", html);
            Assert.Contains(first.ToString(), html);
            Assert.Contains(second.ToString(), html);
        }
        foreach (var title in new[] { " ", new string('x', 256) })
        {
            await PostForm("/QuestionCategories/Create", new Dictionary<string, string>
            {
                ["Title"] = title, ["TotalCategoryId"] = first.ToString()
            });
        }
        await PostForm("/QuestionCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "Invalid order", ["TotalCategoryId"] = first.ToString(), ["OrderIndex"] = "-1"
        });
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(0, await db.QuestionCategories.CountAsync());
            var period = new QuestionCategory { Title = "Original", TotalCategoryId = first };
            db.QuestionCategories.Add(period);
            await db.SaveChangesAsync();
            id = period.Id;
        }
        var invalidEdit = await PostForm($"/QuestionCategories/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Title"] = "Replacement", ["TotalCategoryId"] = Guid.NewGuid().ToString(), ["IsFree"] = "true"
        });
        Assert.AreEqual(HttpStatusCode.OK, invalidEdit.StatusCode);
        using var verify = Server!.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<WeChatExamDbContext>().QuestionCategories.SingleAsync();
        Assert.AreEqual("Original", saved.Title);
        Assert.AreEqual(first, saved.TotalCategoryId);
        Assert.IsFalse(saved.IsFree);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/QuestionCategories?totalCategoryId={Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/QuestionCategories/Edit/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task EmptyCoursesGiveAnActionableStartAndManagementIsProtected()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/QuestionCategories")).StatusCode);
        await RegisterAndLoginAsync();
        foreach (var url in new[] { "/QuestionCategories", "/QuestionCategories/Create", $"/QuestionCategories/Edit/{Guid.NewGuid()}" })
        {
            var response = await Http.GetAsync(url);
            Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden ||
                          response.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        }
        await LoginAsAdmin();
        var empty = await Http.GetStringAsync("/QuestionCategories");
        Assert.Contains("/TotalCategories/Create", empty);
        var (first, _) = await CreateCourses();
        var noToken = await PostForm("/QuestionCategories/Create", new Dictionary<string, string>
        {
            ["Title"] = "Not saved", ["TotalCategoryId"] = first.ToString()
        }, includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(0, await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().QuestionCategories.CountAsync());
    }

    [TestMethod]
    public async Task SchemaUpgradePreservesLegacyDataAndEnforcesCourseOwnership()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddQuestionCategories", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, index);
        await migrator.MigrateAsync(migrations[index - 1]);
        var total = new TotalCategory { Title = "Music history" };
        var tag = new Tag { DisplayName = "Legacy period", NormalizedName = "LEGACY PERIOD"};
        db.TotalCategories.Add(total);
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.AreEqual(0, await db.QuestionCategories.CountAsync());
        Assert.AreEqual(1, await db.Tags.CountAsync());
        Assert.AreEqual("LEGACY PERIOD", (await db.Tags.SingleAsync()).NormalizedName);
        var period = new QuestionCategory { Title = "Early music", TotalCategoryId = total.Id };
        db.QuestionCategories.Add(period);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.QuestionCategories.Include(c => c.TotalCategory).SingleAsync();
        Assert.AreEqual(total.Id, loaded.TotalCategoryId);
        Assert.AreEqual(total.Title, loaded.TotalCategory!.Title);

        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.TotalCategories.Where(c => c.Id == total.Id).ExecuteDeleteAsync());
        db.QuestionCategories.Add(new QuestionCategory { Title = "No course", TotalCategoryId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.AreEqual(1, await db.QuestionCategories.CountAsync());
        await migrator.MigrateAsync(migrations[index - 1]);
        Assert.AreEqual(total.Id, (await db.TotalCategories.SingleAsync()).Id);
        Assert.AreEqual(tag.Id, (await db.Tags.SingleAsync()).Id);
    }
}
