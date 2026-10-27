using System.Net;
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
        Assert.AreEqual(0, await db.VipProducts.CountAsync());
        values["TotalCategoryId"] = course.Id.ToString();
        using var created = await PostForm("/VipProducts/Create", values);
        AssertRedirect(created, "/VipProducts");
        var product = await db.VipProducts.SingleAsync();
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
        Assert.AreEqual("Updated course VIP", (await db.VipProducts.SingleAsync()).Name);
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
}
