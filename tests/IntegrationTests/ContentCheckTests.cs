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

    private static Question PaidQuestion(Guid? subject, params Tag[] tags) => new()
    {
        Id = Guid.NewGuid(), Content = "Exercise", CategoryId = subject,
        QuestionTags = tags.Select(t => new QuestionTag { QuestionId = Guid.Empty, TagId = t.Id, Tag = t }).ToList()
    };

    [TestMethod]
    public void SubjectVipWinsWhileLegacyQuestionsRequireEveryPaidTag()
    {
        var chinese = Guid.NewGuid();
        var western = Guid.NewGuid();
        var unmapped = new Tag { Id = 1, DisplayName = "Works", NormalizedName = "WORKS", IsFree = false };
        var mapped = new Tag
        {
            Id = 2, DisplayName = "Period", NormalizedName = "PERIOD", IsFree = false,
            Taxonomy = new Taxonomy { Name = "Periods", CategoryTaxonomies = [new() { CategoryId = chinese, TaxonomyId = 1 }, new() { CategoryId = western, TaxonomyId = 1 }] }
        };
        var own = new PaperAccessStatus { ActiveCategoryVips = [chinese] };
        var other = new PaperAccessStatus { ActiveCategoryVips = [western] };
        var q = PaidQuestion(chinese, unmapped, mapped);
        Assert.IsTrue(QuestionAccess.Evaluate(q, true, own).Allowed);
        Assert.IsFalse(QuestionAccess.Evaluate(q, true, other).Allowed);
        Assert.IsFalse(QuestionAccess.Evaluate(q, false, own).Allowed);
        Assert.IsFalse(QuestionAccess.Evaluate(q, true, new() { HasRealExamVip = true }).Allowed);
        Assert.IsFalse(QuestionAccess.CanUseTag(unmapped, own));
        Assert.IsTrue(QuestionAccess.CanUseTag(mapped, other));
        var legacy = PaidQuestion(null, unmapped, mapped);
        Assert.AreEqual(QuestionAccessDenial.Tag, QuestionAccess.Evaluate(legacy, true, own).Denial);
        Assert.IsTrue(QuestionAccess.Evaluate(PaidQuestion(null, mapped), true, other).Allowed);
        unmapped.IsFree = true;
        Assert.IsTrue(QuestionAccess.Evaluate(legacy, true, own).Allowed);
        mapped.IsFree = true;
        Assert.IsTrue(QuestionAccess.Evaluate(legacy, false, new()).Allowed);
    }

    [TestMethod]
    public async Task ScanExplainsUnmappedTagsPublicLessonsAndMissingProductsWithoutChangingData()
    {
        await using var db = Database();
        var subject = new Category { Id = Guid.NewGuid(), Title = "Chinese history", ParentId = null, IsFree = true };
        var other = new Category { Id = Guid.NewGuid(), Title = "Western history", ParentId = null };
        var tag = new Tag { Id = 1, DisplayName = "Works", NormalizedName = "WORKS", IsFree = false };
        var question = new Question { Id = Guid.NewGuid(), Content = "Paid exercise", CategoryId = subject.Id };
        var legacy = new Question { Id = Guid.NewGuid(), Content = "Uncategorized exercise" };
        var free = new Question { Id = Guid.NewGuid(), Content = "Free exercise" };
        var root = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Root", Content = "Public", ParentId = null };
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Overview", Content = "Public", ParentId = root.Id };
        db.AddRange(subject, other, tag, question, legacy, free, root, lesson,
            new QuestionTag { QuestionId = question.Id, TagId = tag.Id },
            new QuestionTag { QuestionId = legacy.Id, TagId = tag.Id },
            new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = question.Id },
            new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = legacy.Id },
            new CategoryKnowledgePoint { KnowledgePointId = lesson.Id, CategoryId = other.Id },
            new VipProduct { Name = "Paused", CategoryId = subject.Id, IsEnabled = false },
            new VipProduct { Name = "Invalid", CategoryId = null });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var report = await Checker(db).ScanAsync([subject.Id], false);
        var tagFinding = report.Findings.Single(f => f.Rule == "TAG001");
        Assert.AreEqual(2, tagFinding.Objects.Count(o => o.Controller == "Questions"));
        Assert.AreEqual(1, tagFinding.Objects.Count(o => o.Controller == "KnowledgePoints"));
        Assert.IsTrue(tagFinding.Objects.Any(o => o.Name == "Root → Overview"));
        Assert.IsTrue(tagFinding.Observations.All(o => !o.Allowed));
        Assert.AreEqual(2, report.Findings.Count(f => f.Rule == "KP001" && f.Severity == CheckSeverity.Information));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "KP002"));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "VIP003"));
        var impossible = report.Findings.Single(f => f.Rule == "Q001");
        Assert.IsTrue(impossible.Observations.All(o => !o.Allowed));
        var missingProduct = report.Findings.Single(f => f.Rule == "VIP002");
        Assert.IsTrue(missingProduct.Observations.Single(o => o.Scenario == "All relevant VIPs").Allowed);
        Assert.IsTrue(missingProduct.Observations.Single(o => o.Scenario == "Custom VIP selection").Allowed);
        Assert.IsTrue(report.Findings.Where(f => f.Rule == "API001").All(f => f.NeedsEngineering));
        Assert.IsFalse(report.Findings.SelectMany(f => f.Objects).Any(o => o.Id == free.Id.ToString()));
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        Assert.IsFalse((await db.Tags.SingleAsync()).IsFree);
        Assert.AreEqual(3, await db.Questions.CountAsync());
    }

    [TestMethod]
    public async Task PaperChecksUseSnapshotsAndIndependentPaperPricing()
    {
        await using var db = Database();
        var subject = new Category { Id = Guid.NewGuid(), Title = "History", ParentId = null, IsFree = true };
        var tag = new Tag { Id = 1, DisplayName = "Paid", NormalizedName = "PAID", IsFree = false };
        var question = new Question { Id = Guid.NewGuid(), Content = "Original", CategoryId = subject.Id };
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
        var product = new VipProduct { Name = "Paused", CategoryId = subject, IsEnabled = false };
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
        db.AddRange(subject, tag);
        await db.SaveChangesAsync();
        for (var i = 0; i < 500; i++)
        {
            var question = new Question { Id = Guid.NewGuid(), Content = $"Exercise {i}", CategoryId = subject.Id };
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
        Assert.AreEqual(1, report.Findings.Count(f => f.Rule == "API001"));
        Assert.IsTrue(report.Findings.Where(f => f.Rule == "Q002").All(f => f.Objects.Count(o => o.Controller == "Questions") == 1));
        Assert.AreEqual(0, db.ChangeTracker.Entries().Count());
        var groups = ContentFindingGroup.Build(report.Findings);
        var shared = groups.Single(g => g.Rule == "Q002");
        Assert.AreEqual(500, shared.Findings.Count);
        Assert.AreEqual(500, shared.Objects.Count(o => o.Controller == "Questions"));
        Assert.AreEqual(500 * report.Scenarios.Count, shared.Observations.Count());
        Assert.AreEqual(shared.Key, ContentFindingGroup.Build(report.Findings.AsEnumerable().Reverse()).Single(g => g.Rule == "Q002").Key);
    }

    [TestMethod]
    public void SharedTagGroupsKeepDifferentQuestionSubjectsSeparate()
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
            finding.Objects.Add(new("Tags", "1", "Shared tag", "CanManageTags"));
            return finding;
        }
        Assert.AreEqual(2, ContentFindingGroup.Build([Finding(firstSubject), Finding(secondSubject)]).Count);
        Assert.AreEqual(1, ContentFindingGroup.Build([Finding(firstSubject), Finding(firstSubject)]).Count);
        var anotherChapter = Finding(firstSubject);
        anotherChapter.CategoryIds.Add(Guid.NewGuid());
        Assert.AreEqual(1, ContentFindingGroup.Build([Finding(firstSubject), anotherChapter]).Count);
    }

}
