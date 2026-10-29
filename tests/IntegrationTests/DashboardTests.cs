using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class DashboardTests : TestBase
{
    [TestMethod]
    public async Task DashboardCountsNewPapersOncePerOwnerAndUsesKnowledgeDirectories()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync("/Dashboard")).StatusCode);
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var before = await Http.GetStringAsync("/Dashboard");
        var mockCount = Count(before, "mock-paper-count");
        var realCount = Count(before, "real-paper-count");
        var statuses = Chart(before, "paper-status").Deserialize<int[]>()!;
        var course = new TotalCategory { Title = "Dashboard course" };
        var institution = new Institution { Title = "Dashboard institution" };
        var mockDraft = new MockPaper { Title = "Mock draft", TotalCategory = course };
        var mockPublished = new MockPaper { Title = "Published mock", TotalCategory = course, Status = PaperStatus.Publishable };
        var mockWithoutVersion = new MockPaper { Title = "No published version", TotalCategory = course, Status = PaperStatus.Publishable };
        var realDraft = new RealPaper { Title = "Real draft", Institution = institution };
        var realPublished = new RealPaper { Title = "Frozen real", Institution = institution, Status = PaperStatus.Frozen };
        var legacy = new Paper { Id = Guid.NewGuid(), Title = "Retained legacy paper", Status = PaperStatus.Frozen };
        var directory = new KnowledgeCategory { Title = "New knowledge directory", TotalCategoryId = course.Id, TotalCategory = course };
        db.AddRange(mockDraft, mockPublished, mockWithoutVersion, realDraft, realPublished, legacy, directory);
        db.Categories.AddRange(Enumerable.Range(0, 3).Select(i => new Category { Id = Guid.NewGuid(), Title = $"Legacy directory {i}", ParentId = null }));
        db.PaperSnapshots.AddRange(
            new PaperSnapshot { Id = Guid.NewGuid(), Title = "Mock v1", MockPaperId = mockPublished.Id, Version = 1 },
            new PaperSnapshot { Id = Guid.NewGuid(), Title = "Mock v2", MockPaperId = mockPublished.Id, Version = 2 },
            new PaperSnapshot { Id = Guid.NewGuid(), Title = "Real v1", RealPaperId = realPublished.Id, Version = 1 },
            new PaperSnapshot { Id = Guid.NewGuid(), Title = "Old version", PaperId = legacy.Id, Version = 1 },
            new PaperSnapshot { Id = Guid.NewGuid(), Title = "Retained deleted source", Version = 1 });
        var tagName = "'</script><script>alert(1)</script>";
        var tag = new Tag { DisplayName = tagName, NormalizedName = tagName.ToUpperInvariant() };
        var question = new Question { Id = Guid.NewGuid(), Content = "Tagged question", QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod() };
        db.QuestionTags.Add(new QuestionTag { Question = question, QuestionId = question.Id, Tag = tag, TagId = tag.Id });
        await db.SaveChangesAsync();

        var html = await Http.GetStringAsync("/Dashboard");
        Assert.AreEqual(mockCount + 3, Count(html, "mock-paper-count"));
        Assert.AreEqual(realCount + 2, Count(html, "real-paper-count"));
        CollectionAssert.AreEqual(new[] { statuses[0] + 3, statuses[1] + 2 }, Chart(html, "paper-status").Deserialize<int[]>()!);
        Assert.AreEqual(await db.KnowledgeCategories.CountAsync(), Count(html, "knowledge-category-count"));
        Assert.Contains("href=\"/MockPapers\"", html);
        Assert.Contains("href=\"/RealPapers\"", html);
        Assert.DoesNotContain("</script><script>alert(1)</script>", html);
        Assert.IsTrue(Chart(html, "top-tags").EnumerateArray().Any(t => t.GetProperty("Name").GetString() == tagName));
        Assert.Contains("/js/dashboard.js", html);
        Assert.AreEqual(5, await db.PaperSnapshots.CountAsync(s => s.Title == "Mock v1" || s.Title == "Mock v2" ||
            s.Title == "Real v1" || s.Title == "Old version" || s.Title == "Retained deleted source"), "Reading the dashboard must preserve all versions.");
    }

    private static int Count(string html, string id) => int.Parse(Regex.Match(html, $"id=\"{id}\">([0-9]+)<").Groups[1].Value);

    private static JsonElement Chart(string html, string name) => JsonDocument.Parse(
        WebUtility.HtmlDecode(Regex.Match(html, $"data-{name}=\"([^\"]*)\"").Groups[1].Value)).RootElement;
}
