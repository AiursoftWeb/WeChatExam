using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Models.ContentChecksViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ContentCheckTests
{
    private static InMemoryContext Database() => new(new DbContextOptionsBuilder<InMemoryContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ContentCheckService Checker(WeChatExamDbContext db) =>
        new(db, new StringLocalizer<ContentCheckService>(new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)));

    [TestMethod]
    public async Task ScanUsesPeriodsAndDirectKnowledgePlacementsWithoutChangingData()
    {
        await using var db = Database();
        var course = new TotalCategory { Title = "Chinese history" };
        var other = new TotalCategory { Title = "Western history" };
        var period = new QuestionCategory { Title = "Paid period", TotalCategoryId = course.Id, TotalCategory = course };
        var freePeriod = new QuestionCategory { Title = "Free period", TotalCategoryId = course.Id, TotalCategory = course, IsFree = true };
        var parent = new KnowledgeCategory { Title = "Free parent", TotalCategoryId = other.Id, TotalCategory = other, IsFree = true };
        var paidDirectory = new KnowledgeCategory { Title = "Overview", TotalCategoryId = other.Id, TotalCategory = other, ParentKnowledgeCategoryId = parent.Id };
        var freeDirectory = new KnowledgeCategory { Title = "Free overview", TotalCategoryId = course.Id, TotalCategory = course, IsFree = true };
        var question = new Question { Id = Guid.NewGuid(), QuestionCategory = period, Content = "Paid exercise without any paid tag" };
        var free = new Question { Id = Guid.NewGuid(), QuestionCategory = freePeriod, Content = "Free exercise with paid tag" };
        var tag = new Tag { Id = 1, DisplayName = "Internal", NormalizedName = "INTERNAL", IsFree = false };
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Mixed placements", Content = "Lesson" };
        var paidLesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Paid lesson", Content = "Paid body" };
        var unplaced = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Draft", Content = "Not public" };
        db.AddRange(course, other, period, freePeriod, parent, paidDirectory, freeDirectory, question, free, tag, lesson, paidLesson, unplaced,
            new QuestionTag { QuestionId = free.Id, TagId = tag.Id, Tag = tag },
            new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = question.Id },
            new KnowledgePointQuestion { KnowledgePointId = paidLesson.Id, QuestionId = question.Id },
            new KnowledgeCategoryKnowledgePoint { KnowledgePointId = lesson.Id, KnowledgeCategoryId = paidDirectory.Id },
            new KnowledgeCategoryKnowledgePoint { KnowledgePointId = lesson.Id, KnowledgeCategoryId = freeDirectory.Id },
            new KnowledgeCategoryKnowledgePoint { KnowledgePointId = paidLesson.Id, KnowledgeCategoryId = paidDirectory.Id },
            new VipProduct { Name = "Paused", TotalCategory = course, IsEnabled = false },
            new VipProduct { Name = "Invalid", TotalCategoryId = null });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var report = await Checker(db).ScanAsync([course.Id, paidDirectory.Id], false);
        Assert.AreEqual(2, report.Categories.Count);
        Assert.AreEqual(2, report.Scenarios.Count(s => s.Name.StartsWith("Subject VIP:")));
        Assert.IsTrue(report.Scenarios.Single(s => s.Name == "Custom VIP selection").Status.ActiveCategoryVips.SetEquals([course.Id]));
        var conflict = report.Findings.Single(f => f.Rule == "KP001");
        Assert.AreEqual(CheckSeverity.Error, conflict.Severity);
        Assert.IsTrue(conflict.Objects.Any(o => o.Controller == "QuestionCategories" && o.Id == period.Id.ToString()));
        Assert.IsTrue(conflict.Objects.Any(o => o.Controller == "KnowledgeCategories" && o.Id == freeDirectory.Id.ToString()));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "KP002"));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "VIP003"));
        Assert.IsTrue(report.Findings.Single(f => f.Rule == "KP003").Objects.Any(o => o.Id == unplaced.Id.ToString()));
        Assert.IsFalse(report.Findings.Any(f => f.Rule is "API001" or "CLIENT001" or "Q001" or "Q002" or "TAG001"));
        var missing = report.Findings.Single(f => f.Rule == "VIP002");
        Assert.IsTrue(missing.Observations.Single(o => o.Scenario == "All relevant VIPs" && o.Entry.Contains("/Questions/")).Allowed);
        var anonymous = report.Exercises[question.Id].Access.First();
        Assert.IsFalse(anonymous.Allowed);
        Assert.IsTrue(anonymous.Knowledge.Single(k => k.KnowledgePoint.Id == lesson.Id.ToString()).Allowed);
        Assert.IsFalse(anonymous.Knowledge.Single(k => k.KnowledgePoint.Id == paidLesson.Id.ToString()).Allowed,
            "A free parent does not grant access to a paid direct placement.");
        var owner = report.Exercises[question.Id].Access.Single(a => a.Scenario == "Subject VIP: Chinese history");
        Assert.IsTrue(owner.Allowed);
        Assert.IsFalse(owner.Knowledge.Single(k => k.KnowledgePoint.Id == paidLesson.Id.ToString()).Allowed);
        var western = report.Exercises[question.Id].Access.Single(a => a.Scenario == "Subject VIP: Western history");
        Assert.IsFalse(western.Allowed);
        Assert.IsTrue(western.Knowledge.All(k => k.Allowed));
        Assert.IsTrue(report.Exercises[free.Id].Access.All(a => a.Allowed));
        Assert.AreEqual(ContentPricing.Public, report.Exercises[free.Id].Question.Pricing);
        Assert.AreEqual(1, report.PeriodImpacts[period.Id].Count(o => o.Controller == "Questions"));
        Assert.AreEqual(2, report.PeriodImpacts[period.Id].Count(o => o.Controller == "KnowledgePoints"));
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        Assert.IsFalse((await db.Tags.SingleAsync()).IsFree);
        Assert.AreEqual(2, await db.Questions.CountAsync());
    }

    [TestMethod]
    public async Task LegacyTagMetadataDoesNotOverrideFreePractice()
    {
        await using var db = Database();
        var period = QuestionPeriodAssignmentTests.TestPeriod(isFree: true);
        var legacyCourse = new Category { Id = period.TotalCategoryId, Title = "Legacy course", ParentId = null };
        var taxonomy = new Taxonomy { Id = 1, Name = "Legacy navigation" };
        var tag = new Tag { Id = 1, DisplayName = "Paid metadata", NormalizedName = "PAID METADATA", IsFree = false, Taxonomy = taxonomy };
        var question = new Question { Id = Guid.NewGuid(), Content = "Free practice", QuestionCategory = period };
        db.AddRange(legacyCourse, period, taxonomy, tag, question,
            new CategoryTaxonomy { CategoryId = legacyCourse.Id, TaxonomyId = taxonomy.Id },
            new QuestionTag { QuestionId = question.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        var report = await Checker(db).ScanAsync([], false);
        var finding = report.Findings.Single(f => f.Rule == "VIP001");
        Assert.IsTrue(finding.NeedsEngineering);
        Assert.IsTrue(finding.Observations.All(o => o.Entry == $"GET /api/miniprogramapi/taxonomies/{taxonomy.Id}/tags"));
        Assert.IsTrue(finding.Observations.Single(o => o.Scenario == "All relevant VIPs").Allowed);
        Assert.IsFalse(finding.Observations.Single(o => o.Scenario == "Anonymous").Allowed);
        Assert.IsTrue(report.Exercises[question.Id].Access.All(a => a.Allowed));
        Assert.IsFalse(report.Findings.Any(f => f.Rule == "VIP002"));
    }

    [TestMethod]
    public async Task PaperChecksUseSnapshotsAndIndependentPaperPricing()
    {
        await using var db = Database();
        var subject = new Category { Id = Guid.NewGuid(), Title = "History", ParentId = null, IsFree = true };
        var tag = new Tag { Id = 1, DisplayName = "Paid", NormalizedName = "PAID", IsFree = false };
        var paidPeriod = QuestionPeriodAssignmentTests.TestPeriod();
        paidPeriod.IsFree = false;
        var question = new Question { QuestionCategory = paidPeriod, Id = Guid.NewGuid(), Content = "Original" };
        var missing = new Paper { Id = Guid.NewGuid(), Title = "Missing", Status = PaperStatus.Publishable, IsFree = true };
        var empty = new Paper { Id = Guid.NewGuid(), Title = "Empty", Status = PaperStatus.Frozen, IsFree = true };
        var real = new Paper { Id = Guid.NewGuid(), Title = "Real exam", Status = PaperStatus.Frozen, IsRealExam = true };
        var impossible = new Paper { Id = Guid.NewGuid(), Title = "No subject", Status = PaperStatus.Publishable };
        var free = new Paper { Id = Guid.NewGuid(), Title = "Free snapshot", Status = PaperStatus.Frozen, IsFree = true };
        var draft = new Paper { Id = Guid.NewGuid(), Title = "Draft", Status = PaperStatus.Draft };
        var retained = new Paper { Id = Guid.NewGuid(), Title = "Original deleted", Status = PaperStatus.Frozen, IsFree = true };
        var emptySnapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Empty", PaperId = empty.Id, Version = 2 };
        var realSnapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Real", PaperId = real.Id, Version = 1 };
        var freeSnapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Free", PaperId = free.Id, Version = 1, IsFree = false };
        var retainedSnapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Retained", PaperId = retained.Id, Version = 1 };
        db.AddRange(subject, tag, question, missing, empty, real, impossible, free, draft, retained,
            emptySnapshot, realSnapshot, freeSnapshot, retainedSnapshot,
            new QuestionTag { QuestionId = question.Id, TagId = tag.Id },
            new QuestionSnapshot { PaperSnapshotId = realSnapshot.Id, Content = "Real question" },
            new QuestionSnapshot { PaperSnapshotId = freeSnapshot.Id, Content = "Copy", OriginalQuestionId = question.Id },
            new QuestionSnapshot { PaperSnapshotId = retainedSnapshot.Id, Content = "Retained copy", OriginalQuestionId = null });
        await db.SaveChangesAsync();
        var report = await Checker(db).ScanAsync([], true);
        bool Has(string rule, Paper paper) => report.Findings.Any(f => f.Rule == rule && f.Objects.Any(o => o.Id == paper.Id.ToString()));
        Assert.IsTrue(Has("PAPER001", missing));
        Assert.IsTrue(Has("PAPER002", empty));
        Assert.IsTrue(Has("PAPER003", impossible));
        Assert.IsTrue(Has("VIP004", real));
        Assert.IsTrue(Has("PAPER004", free));
        Assert.IsFalse(report.Findings.SelectMany(f => f.Objects).Any(o => o.Id == draft.Id.ToString() || o.Id == retained.Id.ToString()));
        var realFinding = report.Findings.Single(f => f.Rule == "VIP004" && f.Objects.Any(o => o.Id == real.Id.ToString()));
        Assert.IsTrue(realFinding.Observations.Single(o => o.Scenario == "Real-exam VIP only").Allowed);
        Assert.IsFalse(realFinding.Observations.Single(o => o.Scenario.StartsWith("Subject VIP:")).Allowed);
        var freeFinding = report.Findings.Single(f => f.Rule == "PAPER004");
        Assert.IsTrue(freeFinding.Observations.Single(o => o.Scenario == "Anonymous").Allowed);
    }

    [TestMethod]
    public async Task OnlyActiveMembershipsGrantAccessEvenWhenProductIsDisabled()
    {
        var subject = Guid.NewGuid();
        var product = new VipProduct { Name = "Paused", TotalCategoryId = subject, IsEnabled = false };
        var pay = new StubWeChatPayService();
        var membership = new VipMembership { UserId = "test", VipProduct = product, StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1) };
        pay.Memberships.Add(membership);
        Assert.IsTrue((await new PaperAccessService(pay).GetUserAccessStatusAsync("test")).ActiveCategoryVips.Contains(subject));
        membership.EndTime = DateTime.UtcNow.AddMinutes(-1);
        Assert.AreEqual(0, (await new PaperAccessService(pay).GetUserAccessStatusAsync("test")).ActiveCategoryVips.Count);
        membership.StartTime = DateTime.UtcNow.AddDays(1);
        membership.EndTime = DateTime.UtcNow.AddDays(2);
        Assert.AreEqual(0, (await new PaperAccessService(pay).GetUserAccessStatusAsync("test")).ActiveCategoryVips.Count);
    }
    [TestMethod]
    public async Task RelationalScanBatchesSharedTagsAndKeepsTheDatabaseReadOnly()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new List<string>();
        var options = new DbContextOptionsBuilder<Sqlite.SqliteContext>().UseSqlite(connection)
            .LogTo(commands.Add, [Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.CommandExecuted]).Options;
        await using var db = new Sqlite.SqliteContext(options);
        await db.Database.EnsureCreatedAsync();
        var subject = new Category { Id = Guid.NewGuid(), Title = "History", ParentId = null, IsFree = true };
        var tag = new Tag { DisplayName = "Shared tag", NormalizedName = "SHARED TAG", IsFree = false };
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        period.IsFree = false;
        db.AddRange(subject, tag);
        await db.SaveChangesAsync();
        for (var i = 0; i < 500; i++)
        {
            var question = new Question { QuestionCategory = period, Id = Guid.NewGuid(), Content = $"Exercise {i}" };
            db.Questions.Add(question);
            db.QuestionTags.Add(new QuestionTag { QuestionId = question.Id, TagId = tag.Id });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        commands.Clear();
        var report = await Checker(db).ScanAsync([], false);
        Assert.IsTrue(commands.Count < 20, $"Expected batched reads, observed {commands.Count} commands.");
        Assert.IsTrue(commands.All(c => !c.Contains("INSERT INTO") && !c.Contains("UPDATE ") && !c.Contains("DELETE FROM")));
        Assert.AreEqual(500, report.TagImpacts[tag.Id].Count(o => o.Controller == "Questions"));
        Assert.AreEqual(0, report.Findings.Count(f => f.Rule == "API001"));
        Assert.IsTrue(report.Findings.Where(f => f.Rule == "VIP002").All(f => f.Objects.Count(o => o.Controller == "Questions") == 1));
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        var groups = ContentFindingGroup.Build(report.Findings);
        var shared = groups.Single(g => g.Rule == "VIP002");
        Assert.AreEqual(500, shared.Findings.Count);
        Assert.AreEqual(500, shared.Objects.Count(o => o.Controller == "Questions"));
        Assert.AreEqual(500 * report.Scenarios.Count, shared.Observations.Count());
        Assert.AreEqual(shared.Key, ContentFindingGroup.Build(report.Findings.AsEnumerable().Reverse()).Single(g => g.Rule == "VIP002").Key);
    }

    [TestMethod]
    public void SharedPeriodGroupsKeepDifferentQuestionSubjectsSeparate()
    {
        var firstSubject = Guid.NewGuid();
        var secondSubject = Guid.NewGuid();
        ContentFinding Finding(Guid subject)
        {
            var finding = new ContentFinding
            {
                Rule = "KP001", Severity = CheckSeverity.Information, Title = "Paid exercise",
                Reason = "Subject VIP required", Suggestion = "Review pricing", QuestionSubjectId = subject
            };
            finding.CategoryIds.UnionWith([firstSubject, secondSubject]);
            finding.Objects.Add(new("QuestionCategories", "1", "Shared period", "CanEditAnyCategory"));
            return finding;
        }
        Assert.AreEqual(2, ContentFindingGroup.Build([Finding(firstSubject), Finding(secondSubject)]).Count);
        Assert.AreEqual(1, ContentFindingGroup.Build([Finding(firstSubject), Finding(firstSubject)]).Count);
        var anotherChapter = Finding(firstSubject);
        anotherChapter.CategoryIds.Add(Guid.NewGuid());
        Assert.AreEqual(1, ContentFindingGroup.Build([Finding(firstSubject), anotherChapter]).Count);
    }

}
