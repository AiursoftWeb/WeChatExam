using Aiursoft.WeChatExam.Authorization;
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
        var tag = new Tag { Id = 1, DisplayName = "Internal", NormalizedName = "INTERNAL"};
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
        Assert.AreEqual("INTERNAL", (await db.Tags.SingleAsync()).NormalizedName);
        Assert.AreEqual(2, await db.Questions.CountAsync());
    }

    [TestMethod]
    public async Task InternalTagMetadataDoesNotOverrideFreePractice()
    {
        await using var db = Database();
        var period = QuestionPeriodAssignmentTests.TestPeriod(isFree: true);
        var taxonomy = new Taxonomy { Id = 1, Name = "Legacy navigation" };
        var tag = new Tag { Id = 1, DisplayName = "Paid metadata", NormalizedName = "PAID METADATA",  Taxonomy = taxonomy };
        var question = new Question { Id = Guid.NewGuid(), Content = "Free practice", QuestionCategory = period };
        db.AddRange(period, taxonomy, tag, question,
            new QuestionTag { QuestionId = question.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        var report = await Checker(db).ScanAsync([], false);
        Assert.IsFalse(report.Findings.Any(f => f.Rule is "TAG001" or "VIP001"));
        Assert.IsFalse(report.Findings.SelectMany(f => f.Observations).Any(o => o.Entry.Contains("/taxonomies")));
        Assert.IsTrue(report.TagImpacts[tag.Id].Any(o => o.Controller == "Questions" && o.Id == question.Id.ToString()));
        Assert.AreEqual(taxonomy.Id.ToString(), report.TagMappings[tag.Id].Taxonomy!.Id);
        Assert.IsTrue(report.Exercises[question.Id].Access.All(a => a.Allowed));
        Assert.IsFalse(report.Findings.Any(f => f.Rule == "VIP002"));
    }

    [TestMethod]
    public async Task LegacyPapersRequireMigrationRegardlessOfStatusOrOldPricing()
    {
        await using var db = Database();
        var draft = new Paper { Id = Guid.NewGuid(), Title = "Draft archive", Status = PaperStatus.Draft };
        var free = new Paper { Id = Guid.NewGuid(), Title = "Free archive", Status = PaperStatus.Publishable, IsFree = true };
        var real = new Paper { Id = Guid.NewGuid(), Title = "Paid archive", Status = PaperStatus.Frozen, IsRealExam = true };
        var snapshot = new PaperSnapshot { Id = Guid.NewGuid(), PaperId = real.Id, Title = "Retained version", Version = 2 };
        db.AddRange(draft, free, real, snapshot,
            new QuestionSnapshot { PaperSnapshotId = snapshot.Id, Content = "Retained copy", OriginalQuestionId = null });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var report = await Checker(db).ScanAsync([], true);
        Assert.AreEqual(3, report.PaperCount);
        Assert.AreEqual(1, report.Counts["Paper snapshots"]);
        Assert.AreEqual(3, report.Findings.Count);
        foreach (var paper in new[] { draft, free, real })
        {
            var finding = report.Findings.Single(f => f.Objects.Any(o => o.Id == paper.Id.ToString()));
            Assert.AreEqual("PAPER005", finding.Rule);
            Assert.AreEqual(CheckSeverity.Warning, finding.Severity);
            Assert.AreEqual(0, finding.Observations.Count, "Retired routes have no current student access scenario.");
            var reference = finding.Objects.Single();
            Assert.AreEqual("Papers", reference.Controller);
            Assert.AreEqual(paper.Status == PaperStatus.Frozen ? "Details" : "Edit", reference.Action);
            Assert.AreEqual(ContentPricing.Unspecified, reference.Pricing);
            Assert.AreEqual(AppPermissionNames.CanEditPapers, reference.Permission);
        }
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        Assert.AreEqual(3, await db.Papers.CountAsync());
        Assert.AreEqual("Retained copy", (await db.QuestionSnapshots.SingleAsync()).Content);
    }

    [TestMethod]
    public async Task IndependentPapersUseCourseOrInstitutionRightsAndRetainedVersions()
    {
        await using var db = Database();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        period.IsFree = false;
        var question = new Question { Id = Guid.NewGuid(), Content = "Paid original", QuestionCategory = period };
        var school = new Institution { Title = "School" };
        var other = new Institution { Title = "Other school" };
        var mock = new MockPaper { Title = "Course mock", TotalCategoryId = period.TotalCategoryId, Status = PaperStatus.Publishable };
        var free = new MockPaper { Title = "Anonymous mock", TotalCategoryId = period.TotalCategoryId, Status = PaperStatus.Frozen, AllowViewAnonymousely = true };
        var real = new RealPaper { Title = "School real", InstitutionId = school.Id, Status = PaperStatus.Frozen };
        var missing = new RealPaper { Title = "Missing version", InstitutionId = school.Id, Status = PaperStatus.Publishable };
        var draft = new RealPaper { Title = "Draft", InstitutionId = other.Id };
        var empty1 = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Empty first", RealPaperId = real.Id, Version = 1 };
        var empty2 = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Empty second", RealPaperId = real.Id, Version = 2 };
        var retained = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Retained", RealPaperId = real.Id, Version = 3, IsFree = true };
        var freeVersion = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Free copy", MockPaperId = free.Id, Version = 1 };
        var mockVersion = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Paid copy", MockPaperId = mock.Id, Version = 1 };
        var paused = new VipProduct { Name = "Paused school", Type = VipProductType.Institution, Institution = school, IsEnabled = false };
        db.AddRange(period, question, school, other, mock, free, real, missing, draft,
            empty1, empty2, retained, freeVersion, mockVersion, paused,
            new VipProduct { Name = "Wrong school", Type = VipProductType.Institution, Institution = other, IsEnabled = true },
            new VipProduct { Name = "Missing school", Type = VipProductType.Institution, IsEnabled = true },
            new QuestionSnapshot { PaperSnapshotId = retained.Id, Content = "Original deleted", OriginalQuestionId = null },
            new QuestionSnapshot { PaperSnapshotId = freeVersion.Id, Content = "Retained paid question", OriginalQuestionId = question.Id },
            new QuestionSnapshot { PaperSnapshotId = mockVersion.Id, Content = "Mock copy" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var report = await Checker(db).ScanAsync([], false, institutions: [school.Id, Guid.NewGuid()]);
        Assert.AreEqual(5, report.PaperCount);
        Assert.AreEqual(2, report.Institutions.Count);
        Assert.IsFalse(report.Exercises[question.Id].Access.Single(a => a.Scenario == "Custom VIP selection").Allowed,
            "Institution membership does not grant independent practice access.");
        Assert.IsTrue(report.Scenarios.Single(s => s.Name == "Custom VIP selection").Status.ActiveInstitutionVips.SetEquals([school.Id]));
        bool Has(string rule, Guid id) => report.Findings.Any(f => f.Rule == rule && f.Objects.Any(o => o.Id == id.ToString()));
        Assert.IsTrue(Has("PAPER001", missing.Id));
        Assert.IsFalse(Has("PAPER001", draft.Id));
        Assert.IsFalse(Has("PAPER003", real.Id));
        Assert.AreEqual(2, ContentFindingGroup.Build(report.Findings).Count(g => g.Rule == "PAPER002"));
        Assert.IsTrue(report.Findings.Where(f => f.Rule == "PAPER002").All(f => f.Observations.All(o => !o.Entry.Contains(retained.Id.ToString()))));
        var schoolFinding = report.Findings.Single(f => f.Rule == "VIP004" && f.Objects[0].Id == real.Id.ToString());
        Assert.AreEqual("RealPapers", schoolFinding.Objects[0].Controller);
        Assert.AreEqual("Versions", schoolFinding.Objects[0].Action);
        Assert.AreEqual(ContentPricing.Paid, schoolFinding.Objects[0].Pricing);
        Assert.AreEqual(0, schoolFinding.CategoryIds.Count);
        Assert.IsTrue(schoolFinding.Objects.Any(o => o.Controller == "Institutions" && o.Id == school.Id.ToString()));
        Assert.IsTrue(schoolFinding.Objects.Any(o => o.Controller == "VipProducts" && o.Id == paused.Id.ToString() && o.Enabled == false));
        Assert.IsTrue(schoolFinding.Observations.Single(o => o.Scenario == "Institution VIP: School").Allowed);
        Assert.IsTrue(schoolFinding.Observations.Single(o => o.Scenario == "Custom VIP selection").Allowed);
        Assert.IsTrue(schoolFinding.Observations.Single(o => o.Scenario == "Real-exam VIP only").Allowed);
        Assert.IsFalse(schoolFinding.Observations.Single(o => o.Scenario.StartsWith("Subject VIP:")).Allowed);
        Assert.IsFalse(schoolFinding.Observations.Single(o => o.Scenario == "Anonymous").Allowed);
        var mockFinding = report.Findings.Single(f => f.Rule == "VIP004" && f.Objects[0].Id == mock.Id.ToString());
        Assert.IsTrue(mockFinding.Observations.Single(o => o.Scenario.StartsWith("Subject VIP:")).Allowed);
        Assert.IsFalse(mockFinding.Observations.Single(o => o.Scenario == "Custom VIP selection").Allowed);
        var opening = report.Findings.Single(f => f.Rule == "PAPER004");
        Assert.AreEqual(CheckSeverity.Information, opening.Severity);
        Assert.IsTrue(opening.Observations.All(o => o.Allowed));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "VIP005"));
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());

        // A product for a different school is insufficient; an enabled product for this
        // school removes the purchase warning without changing historical snapshots.
        var savedProduct = await db.VipProducts.FindAsync(paused.Id);
        savedProduct!.IsEnabled = true;
        await db.SaveChangesAsync();
        report = await Checker(db).ScanAsync([], false);
        Assert.IsFalse(report.Findings.Any(f => f.Rule == "VIP004" && f.Objects[0].Id == real.Id.ToString()));
        Assert.AreEqual(5, await db.PaperSnapshots.CountAsync());
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
        var tag = new Tag { DisplayName = "Shared tag", NormalizedName = "SHARED TAG"};
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
        Assert.IsTrue(commands.Count < 26, $"Expected batched reads, observed {commands.Count} commands.");
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
