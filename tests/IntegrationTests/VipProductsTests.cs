using System.Net;
using Microsoft.Extensions.Options;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Controllers.Management;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.Extensions.Localization;
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
    public async Task RealPaperAndInstitutionEditorsShowTheSavedInstitutionsSalesState()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var institution = new Institution { Title = "School with a paper" };
        var other = new Institution { Title = "Other school" };
        db.Institutions.AddRange(institution, other);
        var paper = new RealPaper { Title = "Paper for sale", Institution = institution, TimeLimit = 180 };
        db.RealPapers.Add(paper);
        await db.SaveChangesAsync();

        var paperPath = $"/RealPapers/Edit/{paper.Id}";
        var noProduct = await Http.GetStringAsync(paperPath);
        Assert.Contains("No VIP product is configured.", noProduct);
        Assert.Contains($"/VipProducts/Create?institutionId={institution.Id}", noProduct);
        Assert.Contains("Saving a new VIP product immediately opens sales.", noProduct);
        var create = await Http.GetStringAsync($"/VipProducts/Create?institutionId={institution.Id}");
        Assert.Contains("School with a paper", create);
        var optionPosition = create.IndexOf($"value=\"{institution.Id}\"", StringComparison.Ordinal);
        Assert.IsTrue(optionPosition >= 0);
        var selectedOption = create[create.LastIndexOf("<option", optionPosition, StringComparison.Ordinal)..create.IndexOf("</option>", optionPosition, StringComparison.Ordinal)];
        Assert.Contains("selected", selectedOption);

        var product = new VipProduct
        {
            Name = "School plan", Type = VipProductType.Institution, InstitutionId = institution.Id,
            PriceInFen = 1234, DurationDays = 90, IsEnabled = false
        };
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();
        var paused = await Http.GetStringAsync(paperPath);
        Assert.Contains("Sales paused", paused);
        Assert.Contains("School plan", paused);
        Assert.Contains("12.34", paused);
        Assert.Contains($"/VipProducts/Edit/{product.Id}", paused);
        Assert.Contains("Sales paused", await Http.GetStringAsync($"/Institutions/Edit/{institution.Id}"));

        Assert.DoesNotContain("School plan", await Http.GetStringAsync($"/VipProducts?institutionId={institution.Id}"));
        var filtered = await Http.GetStringAsync($"/VipProducts?institutionId={institution.Id}&showDisabled=true");
        Assert.Contains("School plan", filtered);
        Assert.DoesNotContain("Other school plan", filtered);
        product.IsEnabled = true;
        db.VipProducts.Add(new VipProduct
        {
            Name = "Other school plan", Type = VipProductType.Institution, InstitutionId = other.Id,
            PriceInFen = 100, DurationDays = 30, IsEnabled = true
        });
        await db.SaveChangesAsync();
        var onSale = await Http.GetStringAsync(paperPath);
        Assert.Contains("On sale", onSale);
        Assert.Contains("School plan", onSale);
        Assert.DoesNotContain("Other school plan", onSale);
        filtered = await Http.GetStringAsync($"/VipProducts?institutionId={institution.Id}");
        Assert.DoesNotContain("Other school plan", filtered);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StatusTabsPreserveCourseOrInstitutionScopeAndSearch(bool institutionScope)
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Music course one" };
        var school = new Institution { Title = "Music school one" };
        var otherCourse = new TotalCategory { Title = "Other music course" };
        var otherSchool = new Institution { Title = "Other music school" };
        db.AddRange(course, school, otherCourse, otherSchool);
        VipProduct Product(string name, bool enabled, bool outside = false) => new()
        {
            Name = name, IsEnabled = enabled, PriceInFen = 100, DurationDays = 30,
            Type = institutionScope ? VipProductType.Institution : VipProductType.Category,
            InstitutionId = institutionScope ? outside ? otherSchool.Id : school.Id : null,
            TotalCategoryId = institutionScope ? null : outside ? otherCourse.Id : course.Id
        };
        db.VipProducts.AddRange(Product("Scope active", true), Product("Scope inactive", false),
            Product("Outside Scope active", true, true), Product("Outside Scope inactive", false, true),
            Product("Unmatched active", true), Product("Unmatched inactive", false));
        await db.SaveChangesAsync();
        var key = institutionScope ? "institutionId" : "totalCategoryId";
        var id = institutionScope ? school.Id : course.Id;
        var html = await Http.GetStringAsync($"/VipProducts?{key}={id}&search=Scope");
        Assert.Contains("Scope active", html);
        Assert.DoesNotContain("Scope inactive", html);
        Assert.DoesNotContain("Outside Scope", html);
        Assert.DoesNotContain("Unmatched", html);
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(html, @"Enabled\s*\(1\)"));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(html, @"Disabled\s*\(1\)"));
        var tabs = System.Text.RegularExpressions.Regex.Matches(html, @"<a\b[^>]*class=""nav-link[^""]*""[^>]*>")
            .Select(m => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(m.Value, @"href=""([^""]+)""").Groups[1].Value))
            .Where(link => link.StartsWith("/VipProducts?", StringComparison.Ordinal)).ToArray();
        Assert.AreEqual(2, tabs.Length);
        var disabledLink = tabs.Single(link => link.Contains("showDisabled=true", StringComparison.Ordinal));
        foreach (var link in tabs)
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(new Uri("http://localhost"), link).Query);
            Assert.AreEqual(id.ToString(), query[key].ToString());
            Assert.AreEqual("Scope", query["search"].ToString());
        }
        var disabled = await Http.GetStringAsync(disabledLink);
        Assert.Contains("Scope inactive", disabled);
        Assert.DoesNotContain("Scope active", disabled);
        Assert.DoesNotContain("Outside Scope", disabled);
        Assert.DoesNotContain("Unmatched", disabled);
        Assert.Contains($"name=\"{key}\" value=\"{id}\"", disabled);
        Assert.Contains("name=\"showDisabled\" value=\"true\"", disabled);
    }

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
    public async Task RetiredRealExamAndForgedTypesCannotBeCreated()
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
        using var retired = await PostForm("/VipProducts/Create", values);
        Assert.AreEqual(HttpStatusCode.OK, retired.StatusCode);
        Assert.Contains("Invalid VIP product type.", await retired.Content.ReadAsStringAsync());
        Assert.IsFalse(await db.VipProducts.AnyAsync(p => p.Name == "All real exams"));
    }

    [TestMethod]
    public async Task AdminCannotGrantRetiredAllSchoolMembership()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = await db.Users.FirstAsync();
        var product = new VipProduct
        {
            Name = "Retired all-school VIP", Type = VipProductType.RealExam,
            IsEnabled = true, PriceInFen = 100, DurationDays = 30
        };
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();

        var form = await Http.GetStringAsync("/VipMembers/Create");
        Assert.DoesNotContain(product.Name, form);
        using var response = await PostForm("/VipMembers/Create", new Dictionary<string, string>
        {
            ["UserId"] = user.Id, ["VipProductId"] = product.Id.ToString(),
            ["StartTime"] = DateTime.UtcNow.ToString("O"),
            ["EndTime"] = DateTime.UtcNow.AddDays(30).ToString("O")
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(0, await db.VipMemberships.CountAsync());
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
        Assert.DoesNotContain("Stopped Wuhan sales", await Http.GetStringAsync("/VipProducts?search=Wuhan%20buyer%20school"));
        Assert.Contains("Stopped Wuhan sales", await Http.GetStringAsync("/VipProducts?showDisabled=true&search=Wuhan%20buyer%20school"));
    }


    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DeleteRetiredProductPreservesAuditIdentity(bool isEnabled)
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = await db.Users.FirstAsync();

        var product = new VipProduct
        {
            Name = "Product with audit history",
            Type = VipProductType.RealExam,
            PriceInFen = 1000,
            DurationDays = 30,
            IsEnabled = isEnabled
        };
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();

        var change = new Change
        {
            Type = ChangeType.VipActivatedViaAdmin,
            TargetUserId = user.Id,
            TriggerUserId = user.Id,
            VipProductId = product.Id,
            Details = "Historical admin grant"
        };
        db.Changes.Add(change);
        await db.SaveChangesAsync();

        using var response = await PostForm($"/VipProducts/Delete/{product.Id}", new Dictionary<string, string>
        {
            ["id"] = product.Id.ToString()
        });
        if (isEnabled)
        {
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("This product has audit history. Disable it instead of deleting it.", await response.Content.ReadAsStringAsync());
        }
        else
        {
            AssertRedirect(response, "/VipProducts");
        }

        db.ChangeTracker.Clear();
        Assert.AreEqual(isEnabled, await db.VipProducts.AnyAsync(p => p.Id == product.Id));
        var reloadedChange = await db.Changes.SingleAsync(c => c.Id == change.Id);
        Assert.AreEqual(isEnabled ? product.Id : null, reloadedChange.VipProductId);
        Assert.AreEqual(isEnabled ? null : product.Id, reloadedChange.ArchivedVipProductId);
        Assert.AreEqual(isEnabled ? null : product.Name, reloadedChange.ArchivedVipProductName);
        Assert.AreEqual(change.TriggerUserId, reloadedChange.TriggerUserId);
        Assert.AreEqual("Historical admin grant", reloadedChange.Details);
        if (!isEnabled)
        {
            var history = await Http.GetStringAsync("/Accounting/History");
            Assert.Contains(product.Name, history);
        }
    }

    [TestMethod]
    public async Task OrdinaryProductWithAuditStillCannotBeDeleted()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = await db.Users.FirstAsync();
        var course = new TotalCategory { Title = "Course with audit" };
        var product = new VipProduct { Name = "Current course", Type = VipProductType.Category,
            TotalCategory = course, IsEnabled = false };
        db.VipProducts.Add(product);
        db.Changes.Add(new Change { Type = ChangeType.VipActivatedViaAdmin,
            TargetUserId = user.Id, VipProduct = product, Details = "Historical grant" });
        await db.SaveChangesAsync();

        using var response = await PostForm($"/VipProducts/Delete/{product.Id}",
            new Dictionary<string, string> { ["id"] = product.Id.ToString() });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This product has audit history. Disable it instead of deleting it.",
            await response.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear();
        Assert.IsTrue(await db.VipProducts.AnyAsync(p => p.Id == product.Id));
        Assert.IsTrue(await db.Changes.AnyAsync(c => c.VipProductId == product.Id));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeleteDoesNotMisreportUnexpectedFailuresAsPurchaseConflicts(bool databaseFailure)
    {
        using var scope = Server!.Services.CreateScope();
        Exception failure = databaseFailure
            ? new DbUpdateException("Database unavailable")
            : new InvalidOperationException("Unexpected operation failure");
        var controller = new VipProductsController(
            new StubVipProductService { DeleteException = failure },
            scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>(),
            scope.ServiceProvider.GetRequiredService<IStringLocalizer<VipProductsController>>(),
            scope.ServiceProvider.GetRequiredService<VirtualPaymentGoodsCatalog>(),
            scope.ServiceProvider.GetRequiredService<IOptions<AppSettings>>());
        try
        {
            await controller.DeleteConfirmed(Guid.NewGuid());
            Assert.Fail("Unexpected failures must reach normal error handling.");
        }
        catch (Exception ex) when (ReferenceEquals(ex, failure))
        {
            Assert.IsFalse(controller.ModelState.Any());
        }
    }

    [TestMethod]
    public async Task DeleteUnusedProductStillSucceeds()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var product = new VipProduct { Name = "Unused product", Type = VipProductType.RealExam };
        db.VipProducts.Add(product);
        await db.SaveChangesAsync();
        using var response = await PostForm($"/VipProducts/Delete/{product.Id}", new Dictionary<string, string>
        {
            ["id"] = product.Id.ToString()
        });
        AssertRedirect(response, "/VipProducts");
        db.ChangeTracker.Clear();
        Assert.IsFalse(await db.VipProducts.AnyAsync(p => p.Id == product.Id));
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
