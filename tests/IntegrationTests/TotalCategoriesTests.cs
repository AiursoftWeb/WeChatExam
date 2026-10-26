using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TotalCategoriesTests : TestBase
{
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
        var product = new VipProduct { Name = "Music membership", CategoryId = root.Id, DurationDays = 366, PriceInFen = 19800 };
        db.Categories.AddRange(root, child);
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();
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
        Assert.AreEqual(root.Id, savedProduct.CategoryId);
        Assert.AreEqual(366, savedProduct.DurationDays);
        Assert.AreEqual(19800, savedProduct.PriceInFen);
        await migrator.MigrateAsync();
        Assert.AreEqual(1, await db.TotalCategories.CountAsync());
    }
}
