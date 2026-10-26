using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionPeriodAssignmentTests : TestBase
{
    internal static QuestionCategory TestPeriod()
    {
        var total = new TotalCategory { Title = "Test music course" };
        return new QuestionCategory { Title = "Test period", TotalCategoryId = total.Id, TotalCategory = total };
    }

    internal static async Task<Guid> AddPeriod(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var total = new TotalCategory { Title = "Test music course" };
        var period = new QuestionCategory { Title = "Test period", TotalCategoryId = total.Id, TotalCategory = total };
        db.QuestionCategories.Add(period);
        await db.SaveChangesAsync();
        return period.Id;
    }

    private async Task<(Guid Question, Guid First, Guid Second)> SeedQuestion()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Chinese music history" };
        var other = new TotalCategory { Title = "Western music history" };
        var first = new QuestionCategory { Title = "Early music", TotalCategory = course, TotalCategoryId = course.Id };
        var second = new QuestionCategory { Title = "Medieval music", TotalCategory = other, TotalCategoryId = other.Id };
        var legacy = new Category { Id = Guid.NewGuid(), Title = "Legacy classification", ParentId = null };
        var tag = new Tag { DisplayName = "Paid label", NormalizedName = "PAID LABEL", IsFree = false };
        var question = new Question
        {
            Id = Guid.NewGuid(), Content = "Question for classification", QuestionCategory = first, Category = legacy,
            CategoryId = legacy.Id, StandardAnswer = "Original answer", Explanation = "Original explanation",
            Metadata = "Original metadata", OrderIndex = 12
        };
        db.QuestionCategories.AddRange(first, second);
        db.Questions.Add(question);
        db.QuestionTags.Add(new QuestionTag { Question = question, QuestionId = question.Id, Tag = tag, TagId = tag.Id });
        await db.SaveChangesAsync();
        question.Embedding = [1, 2, 3];
        question.LastUpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.SaveChangesAsync();
        return (question.Id, first.Id, second.Id);
    }

    [TestMethod]
    public async Task AssigningAndChangingPeriodPreservesQuestionContentAndLegacyRules()
    {
        await LoginAsAdmin();
        var (id, first, second) = await SeedQuestion();
        var page = await Http.GetStringAsync($"/Questions/Period/{id}");
        Assert.Contains("Chinese music history", page);
        Assert.Contains("Western music history", page);
        foreach (var period in new[] { first, second })
        {
            var response = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
            {
                ["Id"] = id.ToString(), ["QuestionCategoryId"] = period.ToString(),
                ["Content"] = "Must not overwrite", ["Tags"] = "Must not change",
                ["ReturnUrl"] = "/Questions?page=2"
            });
            AssertRedirect(response, "/Questions?page=2");
            using var scope = Server!.Services.CreateScope();
            var q = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Questions
                .Include(q => q.Category).Include(q => q.QuestionCategory).ThenInclude(c => c!.TotalCategory)
                .Include(q => q.QuestionTags).ThenInclude(t => t.Tag).SingleAsync(q => q.Id == id);
            Assert.AreEqual(period, q.QuestionCategoryId);
            Assert.AreEqual("Question for classification", q.Content);
            Assert.AreEqual("Original answer", q.StandardAnswer);
            Assert.AreEqual("Original explanation", q.Explanation);
            Assert.AreEqual("Original metadata", q.Metadata);
            Assert.AreEqual("Legacy classification", q.Category!.Title);
            Assert.AreEqual("Paid label", q.QuestionTags.Single().Tag.DisplayName);
            Assert.IsFalse(q.QuestionTags.Single().Tag.IsFree);
            Assert.AreEqual(12m, q.OrderIndex);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, q.Embedding!);
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), q.LastUpdatedAt);
        }
        var details = await Http.GetStringAsync($"/Questions/Details/{id}");
        Assert.Contains("Medieval music", details);
        Assert.Contains($"/Questions/Period/{id}", details);
    }

    [TestMethod]
    public async Task InvalidAssignmentsKeepTheFormAndCannotClearOrRedirectAway()
    {
        await LoginAsAdmin();
        var (id, first, _) = await SeedQuestion();
        foreach (var invalid in new[] { "", Guid.Empty.ToString(), Guid.NewGuid().ToString(), "invalid" })
        {
            var response = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
            {
                ["Id"] = id.ToString(), ["QuestionCategoryId"] = invalid,
                ["ReturnUrl"] = "https://example.org/"
            });
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("Question for classification", html);
            Assert.Contains(first.ToString(), html);
            Assert.DoesNotContain("href=\"https://example.org/", html);
        }
        var saved = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["QuestionCategoryId"] = first.ToString(), ["ReturnUrl"] = "//example.org/"
        });
        AssertRedirect(saved, $"/Questions/Details/{id}");
        var cleared = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["QuestionCategoryId"] = ""
        });
        Assert.AreEqual(HttpStatusCode.OK, cleared.StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(first, (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Questions.SingleAsync(q => q.Id == id)).QuestionCategoryId);
        var mismatch = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
        {
            ["Id"] = Guid.NewGuid().ToString(), ["QuestionCategoryId"] = first.ToString()
        });
        Assert.AreEqual(HttpStatusCode.NotFound, mismatch.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/Questions/Period/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task PeriodAssignmentRequiresQuestionEditPermissionAndCsrf()
    {
        var (id, first, _) = await SeedQuestion();
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/Questions/Period/{id}")).StatusCode);
        await RegisterAndLoginAsync();
        var denied = await Http.GetAsync($"/Questions/Period/{id}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        var deniedPost = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["QuestionCategoryId"] = first.ToString()
        }, tokenUrl: "/");
        Assert.IsTrue(deniedPost.StatusCode == HttpStatusCode.Forbidden || deniedPost.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        await LoginAsAdmin();
        var noToken = await PostForm($"/Questions/Period/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["QuestionCategoryId"] = first.ToString()
        }, includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(first, (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Questions.SingleAsync(q => q.Id == id)).QuestionCategoryId);
    }

    [TestMethod]
    public async Task PeriodFiltersKeepEveryQuestionReachable()
    {
        await LoginAsAdmin();
        var (seeded, first, second) = await SeedQuestion();
        Guid other;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (var i = 0; i < 12; i++)
                db.Questions.Add(new Question { Id = Guid.NewGuid(), Content = $"Period question {i}", QuestionCategoryId = first, CreationTime = DateTime.UtcNow.AddMinutes(i) });
            var q = new Question { Id = Guid.NewGuid(), Content = "Other course question", QuestionCategoryId = second };
            other = q.Id;
            db.Questions.Add(q);
            await db.SaveChangesAsync();
        }
        var ids = new HashSet<string>();
        for (var page = 1; page <= 2; page++)
        {
            var html = await Http.GetStringAsync($"/Questions?period={first}&pageSize=10&page={page}");
            Assert.DoesNotContain($"/Questions/Period/{other}", html);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(html, @"/Questions/Period/([a-f0-9-]{36})"))
                Assert.IsTrue(ids.Add(match.Groups[1].Value));
        }
        Assert.AreEqual(13, ids.Count);
        Assert.IsTrue(ids.Contains(seeded.ToString()));
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync("/Questions?period=unassigned")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync("/Questions?period=invalid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/Questions?period={Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task RelationalUpgradePreservesLegacyQuestionsAndRestrictsPeriodDeletion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, m => m.EndsWith("_AddQuestionPeriodAssignment", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, index);
        await migrator.MigrateAsync(migrations[index - 1]);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Questions (Id,Content,QuestionType,GradingStrategy,Metadata,StandardAnswer,Explanation,CreationTime,LastUpdatedAt,LastEmbeddedAt) VALUES ({id},'Preserved question',0,0,'','Preserved answer','',CURRENT_TIMESTAMP,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)");
        await migrator.MigrateAsync(migrations[index]);
        // Existing unclassified data must stop the upgrade, never receive a fabricated default.
        await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAsync());
        await using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT StandardAnswer FROM Questions WHERE QuestionCategoryId IS NULL";
            Assert.AreEqual("Preserved answer", await read.ExecuteScalarAsync());
        }
        var period = TestPeriod();
        db.QuestionCategories.Add(period);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Questions SET QuestionCategoryId={period.Id} WHERE Id={id}");
        await migrator.MigrateAsync();
        var question = await db.Questions.SingleAsync();
        Assert.AreEqual(period.Id, question.QuestionCategoryId);
        Assert.AreEqual("Preserved answer", question.StandardAnswer);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE Questions SET QuestionCategoryId=NULL"));
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<SqliteException>(() => db.QuestionCategories.Where(c => c.Id == period.Id).ExecuteDeleteAsync());
        question = await db.Questions.SingleAsync();
        question.QuestionCategoryId = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await migrator.MigrateAsync(migrations[index - 1]);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StandardAnswer FROM Questions";
        Assert.AreEqual("Preserved answer", await command.ExecuteScalarAsync());
        Assert.AreEqual(1, await db.QuestionCategories.CountAsync());
    }
    [TestMethod]
    public async Task QuestionEditorRequiresARealPeriodAndPreservesLegacyFields()
    {
        await LoginAsAdmin();
        var (id, first, second) = await SeedQuestion();
        var form = new Dictionary<string, string>
        {
            ["Content"] = "New classified question", ["QuestionType"] = "1", ["GradingStrategy"] = "0",
            ["StandardAnswer"] = "Answer", ["ReturnUrl"] = "/Questions?period=" + first,
            ["CategoryId"] = Guid.NewGuid().ToString()
        };
        foreach (var invalid in new[] { "", "invalid", Guid.Empty.ToString(), Guid.NewGuid().ToString() })
        {
            form["QuestionCategoryId"] = invalid;
            var response = await PostForm("/Questions/Create", form);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("New classified question", html);
            Assert.Contains(first.ToString(), html);
            Assert.DoesNotContain("name=\"CategoryId\"", html);
        }
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Content == form["Content"]));
        form["QuestionCategoryId"] = first.ToString();
        AssertRedirect(await PostForm("/Questions/Create", form), form["ReturnUrl"]);
        var created = await db.Questions.SingleAsync(q => q.Content == form["Content"]);
        Assert.AreEqual(first, created.QuestionCategoryId);
        Assert.IsNull(created.CategoryId, "The editor must not accept a legacy category from the client.");
        var original = await db.Questions.AsNoTracking().SingleAsync(q => q.Id == id);
        form["Id"] = id.ToString();
        form["Content"] = original.Content;
        form["QuestionCategoryId"] = second.ToString();
        AssertRedirect(await PostForm($"/Questions/Edit/{id}", form), form["ReturnUrl"]);
        db.ChangeTracker.Clear();
        var updated = await db.Questions.SingleAsync(q => q.Id == id);
        Assert.AreEqual(second, updated.QuestionCategoryId);
        Assert.AreEqual(original.CategoryId, updated.CategoryId);
        Assert.AreEqual("Paid label", (await db.QuestionTags.Include(t => t.Tag).SingleAsync(t => t.QuestionId == id)).Tag.DisplayName);
        form["QuestionCategoryId"] = "";
        form["Content"] = "Must not be saved";
        Assert.AreEqual(HttpStatusCode.OK, (await PostForm($"/Questions/Edit/{id}", form)).StatusCode);
        db.ChangeTracker.Clear();
        updated = await db.Questions.SingleAsync(q => q.Id == id);
        Assert.AreEqual(second, updated.QuestionCategoryId);
        Assert.AreEqual(original.Content, updated.Content);
        var page = await Http.GetStringAsync($"/Questions/Edit/{id}");
        Assert.Contains("name=\"QuestionCategoryId\"", page);
        Assert.DoesNotContain("name=\"CategoryId\"", page);
        Assert.DoesNotContain("Legacy classification", page);
    }

    [TestMethod]
    public async Task TotalCategoryFiltersUsePeriodsRatherThanTheLegacyTree()
    {
        await LoginAsAdmin();
        var (id, first, second) = await SeedQuestion();
        Guid total; Guid otherId; Guid legacyId;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            total = (await db.QuestionCategories.SingleAsync(c => c.Id == first)).TotalCategoryId;
            var q = await db.Questions.SingleAsync(q => q.Id == id);
            legacyId = q.CategoryId!.Value;
            q.QuestionCategoryId = first;
            var other = new Question { Id = Guid.NewGuid(), Content = "Other total question", QuestionCategoryId = second, CategoryId = legacyId };
            otherId = other.Id;
            db.Questions.Add(other); await db.SaveChangesAsync();
        }
        var html = await Http.GetStringAsync($"/Questions?totalCategoryId={total}");
        Assert.Contains($"/Questions/Details/{id}", html);
        Assert.DoesNotContain($"/Questions/Details/{otherId}", html);
        Assert.DoesNotContain("name=\"categoryId\"", html);
        Assert.Contains("question-period-filter.js", html);
        Assert.Contains("name=\"totalCategoryId\"", html);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/Questions?totalCategoryId={total}&period={second}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync("/Questions?totalCategoryId=invalid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/Questions?totalCategoryId={Guid.NewGuid()}")).StatusCode);
        var references = await Http.GetStringAsync($"/Questions?legacyCategoryId={legacyId}");
        Assert.Contains("This link shows legacy category references.", references);
        Assert.Contains($"/Questions/Details/{id}", references);
        Assert.Contains($"/Questions/Details/{otherId}", references);
        var details = await Http.GetStringAsync($"/Questions/Details/{id}");
        Assert.DoesNotContain("Legacy classification", details);
        var deletion = await Http.GetStringAsync($"/Questions/Delete/{id}");
        Assert.Contains("Early music", deletion);
        Assert.DoesNotContain("Legacy classification", deletion);
    }

}
