using System.Net;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class VipProductsTests : TestBase
{
    [TestMethod]
    public async Task EditorOffersOnlyTotalCategoriesAndRejectsKnowledgeDirectoryIds()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Course to purchase" };
        var chapter = new Category { Id = Guid.NewGuid(), Title = "Knowledge overview", ParentId = null };
        db.AddRange(course, chapter);
        await db.SaveChangesAsync();
        var html = await Http.GetStringAsync("/VipProducts/Create");
        Assert.Contains("Course to purchase", html);
        Assert.DoesNotContain("Knowledge overview", html);
        Assert.Contains("name=\"TotalCategoryId\"", html);
        var values = new Dictionary<string, string>
        {
            ["Name"] = "Course VIP", ["Type"] = "0", ["TotalCategoryId"] = chapter.Id.ToString(),
            ["PriceInFen"] = "100", ["DurationDays"] = "365"
        };
        using var invalid = await PostForm("/VipProducts/Create", values);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Select an existing total category.", await invalid.Content.ReadAsStringAsync());
        Assert.IsFalse(await db.VipProducts.AnyAsync(p => p.Name == "Course VIP"));
        values["TotalCategoryId"] = course.Id.ToString();
        using var created = await PostForm("/VipProducts/Create", values);
        AssertRedirect(created, "/VipProducts");
        var product = await db.VipProducts.SingleAsync(p => p.Name == "Course VIP");
        Assert.AreEqual(course.Id, product.TotalCategoryId);
        Assert.AreEqual(VipProductType.Category, product.Type);

        const string origin = "/ContentChecks/Results?view=information&page=2";
        var editPath = $"/VipProducts/Edit/{product.Id}";
        values["Id"] = product.Id.ToString();
        values["checkReturnUrl"] = origin;
        values["TotalCategoryId"] = chapter.Id.ToString();
        using var invalidEdit = await PostForm(editPath, values);
        var invalidHtml = await invalidEdit.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, invalidEdit.StatusCode);
        Assert.Contains(WebUtility.HtmlEncode(origin), invalidHtml);
        Assert.Contains("Course to purchase", invalidHtml);
        values["TotalCategoryId"] = course.Id.ToString();
        values["Name"] = "Updated course VIP";
        using var saved = await PostForm(editPath, values);
        AssertRedirect(saved, origin);
        db.ChangeTracker.Clear();
        Assert.AreEqual("Updated course VIP", (await db.VipProducts.SingleAsync(p => p.Id == product.Id)).Name);
    }

    [TestMethod]
    public async Task RealExamProductHasNoCourseAndForgedTypesCannotBeSaved()
    {
        await LoginAsAdmin();
        var values = new Dictionary<string, string>
        {
            ["Name"] = "All real exams", ["Type"] = "99", ["TotalCategoryId"] = Guid.NewGuid().ToString(),
            ["PriceInFen"] = "100", ["DurationDays"] = "365"
        };
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        using var invalid = await PostForm("/VipProducts/Create", values);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.IsFalse(await db.VipProducts.AnyAsync(p => p.Name == "All real exams"));
        values["Type"] = "1";
        using var created = await PostForm("/VipProducts/Create", values);
        AssertRedirect(created, "/VipProducts");
        var product = await db.VipProducts.SingleAsync(p => p.Name == "All real exams");
        Assert.IsNull(product.TotalCategoryId);
        Assert.AreEqual(VipProductType.RealExam, product.Type);
    }

    [TestMethod]
    public async Task InstitutionEditorValidatesScopeAndProtectsPurchasedTargets()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var institution = new Institution { Title = "Wuhan buyer school" };
        var other = new Institution { Title = "Other buyer school" };
        db.AddRange(institution, other);
        await db.SaveChangesAsync();
        var html = await Http.GetStringAsync("/VipProducts/Create");
        Assert.Contains("Wuhan buyer school", html);
        Assert.Contains("Institution VIP", html);
        var values = new Dictionary<string, string>
        {
            ["Name"] = "Wuhan membership", ["Type"] = "2", ["PriceInFen"] = "100", ["DurationDays"] = "365"
        };
        using var invalid = await PostForm("/VipProducts/Create", values);
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Select an existing institution.", await invalid.Content.ReadAsStringAsync());
        values["InstitutionId"] = institution.Id.ToString();
        using var created = await PostForm("/VipProducts/Create", values);
        AssertRedirect(created, "/VipProducts");
        var product = await db.VipProducts.SingleAsync(p => p.Name == "Wuhan membership");
        Assert.AreEqual(institution.Id, product.InstitutionId);
        Assert.IsNull(product.TotalCategoryId);
        var user = await db.Users.FirstAsync();
        db.PaymentOrders.Add(new PaymentOrder { UserId = user.Id, VipProductId = product.Id, OutTradeNo = "Scope-lock", Description = "Wuhan", Status = PaymentOrderStatus.Pending });
        await db.SaveChangesAsync();
        const string origin = "/ContentChecks/Results?view=information&page=2";
        var editPath = $"/VipProducts/Edit/{product.Id}";
        var locked = await Http.GetStringAsync(editPath);
        Assert.Contains("Access scope is locked", locked);
        values["Id"] = product.Id.ToString();
        values["InstitutionId"] = other.Id.ToString();
        values["checkReturnUrl"] = origin;
        using var forged = await PostForm(editPath, values);
        Assert.AreEqual(HttpStatusCode.OK, forged.StatusCode);
        var rejected = await forged.Content.ReadAsStringAsync();
        Assert.Contains("Create a new product to change its access scope.", rejected);
        Assert.Contains(WebUtility.HtmlEncode(origin), rejected);
        db.ChangeTracker.Clear();
        Assert.AreEqual(institution.Id, (await db.VipProducts.SingleAsync(p => p.Id == product.Id)).InstitutionId);
        values["InstitutionId"] = institution.Id.ToString();
        values["Name"] = "Stopped Wuhan sales";
        using var saved = await PostForm(editPath, values);
        AssertRedirect(saved, origin);
        using var deleted = await PostForm($"/VipProducts/Delete/{product.Id}", new Dictionary<string, string> { ["id"] = product.Id.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Contains("Disable it instead of deleting it.", await deleted.Content.ReadAsStringAsync());
        Assert.IsTrue(await db.VipProducts.AnyAsync(p => p.Id == product.Id));
        Assert.Contains("Stopped Wuhan sales", await Http.GetStringAsync("/VipProducts?search=Wuhan%20buyer%20school"));
    }


    [TestMethod]
    public async Task InstitutionMigrationPreservesPurchasedProductsAndRejectsInvalidTargets()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddInstitutionVipProducts", StringComparison.Ordinal));
        await migrator.MigrateAsync(migrations[index - 1]);
        var id = Guid.NewGuid();
        // Use the historical schema before the new column exists.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO VipProducts (Id,Name,Type,TotalCategoryId,PriceInFen,DurationDays,IsEnabled,CreationTime) VALUES ({id},'Legacy all-school',1,NULL,100,365,0,{DateTime.UtcNow})");
        await migrator.MigrateAsync();
        var retained = await db.VipProducts.SingleAsync();
        Assert.AreEqual(id, retained.Id);
        Assert.AreEqual(VipProductType.RealExam, retained.Type);
        Assert.IsNull(retained.InstitutionId);
        Assert.AreEqual(365, retained.DurationDays);
        Assert.IsFalse(retained.IsEnabled);
        var institution = new Institution { Title = "Valid school" };
        var valid = new VipProduct { Name = "School", Type = VipProductType.Institution, Institution = institution };
        db.VipProducts.Add(valid);
        await db.SaveChangesAsync();
        foreach (var invalid in new[]
        {
            new VipProduct { Name = "No school", Type = VipProductType.Institution },
            new VipProduct { Name = "Unknown school", Type = VipProductType.Institution, InstitutionId = Guid.NewGuid() },
            new VipProduct { Name = "Mixed course", Type = VipProductType.Institution, InstitutionId = institution.Id, TotalCategory = new TotalCategory { Title = "Course" } },
            new VipProduct { Name = "Legacy cannot target school", Type = VipProductType.RealExam, InstitutionId = institution.Id }
        })
        {
            db.VipProducts.Add(invalid);
            await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
        }
        db.Institutions.Remove(await db.Institutions.SingleAsync());
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.AreEqual(2, await db.VipProducts.CountAsync());
    }
}
