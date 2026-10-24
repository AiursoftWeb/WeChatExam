using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class PaperCollectionTests
{
    private SqliteConnection _connection = null!;
    private SqliteContext _db = null!;
    private PaperCollectionService _service = null!;
    private readonly ResourceManagerStringLocalizerFactory _localizers = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
    private int _taxonomy;
    private int _tag;
    private Guid _paper;

    [TestInitialize]
    public async Task Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        _service = new PaperCollectionService(_db, new StringLocalizer<PaperCollectionService>(_localizers));
        var taxonomy = new Taxonomy { Name = "Existing student directory" };
        var tag = new Tag { DisplayName = "Suzhou 21-26", NormalizedName = "SUZHOU 21-26", Taxonomy = taxonomy, IsFree = false, OrderIndex = 4 };
        var paper = new Paper { Id = Guid.NewGuid(), Title = "2025 paper", TimeLimit = 180, IsFree = false, IsRealExam = true, Status = PaperStatus.Publishable };
        var question = new Question { Id = Guid.NewGuid(), Content = "Original question", StandardAnswer = "Answer" };
        _db.Tags.Add(tag); _db.Papers.Add(paper); _db.Questions.Add(question);
        _db.PaperTags.Add(new PaperTag { PaperId = paper.Id, TagId = 0, Paper = paper, Tag = tag });
        _db.QuestionTags.Add(new QuestionTag { QuestionId = question.Id, TagId = 0, Question = question, Tag = tag });
        _db.PaperSnapshots.Add(new PaperSnapshot { Id = Guid.NewGuid(), Paper = paper, Title = paper.Title, TimeLimit = 180, Version = 1, IsFree = false });
        await _db.SaveChangesAsync();
        _taxonomy = taxonomy.Id; _tag = tag.Id; _paper = paper.Id;
    }

    [TestCleanup]
    public async Task Cleanup() { await _db.DisposeAsync(); await _connection.DisposeAsync(); }

    private async Task<PaperCollection> Connect()
    {
        var preview = await _service.PreviewAsync(_taxonomy);
        await _service.ImportAsync(_taxonomy, preview.Fingerprint);
        return await _db.PaperCollections.AsNoTracking().SingleAsync();
    }

    [TestMethod]
    public async Task InvalidCollectionSelectionRollsBackThePaperCreatedInTheSameEditor()
    {
        var paperId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExecuteAtomicAsync(async () =>
        {
            _db.Papers.Add(new Paper { Id = paperId, Title = "Must roll back" });
            await _db.SaveChangesAsync();
            await _service.StagePaperCollectionsAsync(paperId, [Guid.NewGuid()]);
        }));
        _db.ChangeTracker.Clear();
        Assert.IsFalse(await _db.Papers.AnyAsync(p => p.Id == paperId));
    }

    [TestMethod]
    public async Task ConnectPreservesSharedQuestionsFeesSnapshotsAndEmptyEntries()
    {
        _db.Tags.Add(new Tag { DisplayName = "Empty directory", NormalizedName = "EMPTY DIRECTORY", TaxonomyId = _taxonomy, IsFree = true });
        await _db.SaveChangesAsync();
        var snapshot = await _db.PaperSnapshots.AsNoTracking().SingleAsync();
        var preview = await _service.PreviewAsync(_taxonomy);
        Assert.AreEqual(0, await _db.PaperCollections.CountAsync(), "Preview must not migrate data.");
        await _service.ImportAsync(_taxonomy, preview.Fingerprint);
        Assert.AreEqual(2, await _db.PaperCollections.CountAsync());
        Assert.AreEqual(1, await _db.PaperCollectionPapers.CountAsync());
        Assert.AreEqual(1, await _db.QuestionTags.CountAsync(q => q.TagId == _tag));
        Assert.AreEqual(1, await _db.PaperTags.CountAsync(t => t.TagId == _tag));
        Assert.IsFalse((await _db.Tags.FindAsync(_tag))!.IsFree);
        Assert.IsFalse((await _db.Papers.FindAsync(_paper))!.IsFree);
        Assert.AreEqual(snapshot.Id, (await _db.PaperSnapshots.SingleAsync()).Id);
        Assert.AreEqual(180, (await _db.PaperSnapshots.SingleAsync()).TimeLimit);
        Assert.AreEqual(2, (await _service.LegacyTagsAsync(_taxonomy))!.Count);
    }

    [TestMethod]
    public async Task StalePreviewCannotSilentlyAdoptChangedMemberships()
    {
        var preview = await _service.PreviewAsync(_taxonomy);
        _db.PaperTags.RemoveRange(_db.PaperTags); await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ImportAsync(_taxonomy, preview.Fingerprint));
        Assert.AreEqual(0, await _db.PaperCollections.CountAsync());
        Assert.AreEqual(0, await _db.LegacyPaperCollectionCatalogs.CountAsync());
    }

    [TestMethod]
    public async Task RenameKeepsOldNamesAndMembershipChangesMirrorOnlyDirectoryTags()
    {
        var collection = await Connect();
        await _service.SaveAsync(collection.Id, collection.Revision, "Suzhou updated", 2, true, false, [_paper]);
        Assert.AreEqual(collection.Id, (await _service.ResolveLegacyNameAsync("Suzhou 21-26"))!.Id);
        Assert.AreEqual(collection.Id, (await _service.ResolveLegacyNameAsync("Suzhou updated"))!.Id);
        Assert.AreEqual(_tag, (await _db.LegacyPaperCollectionBindings.SingleAsync()).TagId);
        var second = await _service.SaveAsync(null, Guid.Empty, "Mixed mock papers", 3, true, false, [_paper]);
        Assert.AreEqual(2, await _db.PaperCollectionPapers.CountAsync(p => p.PaperId == _paper));
        var source = new Tag { DisplayName = "Source year", NormalizedName = "SOURCE YEAR" };
        _db.PaperTags.Add(new PaperTag { PaperId = _paper, TagId = 0, Tag = source }); await _db.SaveChangesAsync();
        await _service.SetPaperCollectionsAsync(_paper, [second]);
        Assert.AreEqual(1, await _db.PaperCollectionPapers.CountAsync());
        Assert.IsFalse(await _db.PaperTags.AnyAsync(t => t.TagId == _tag));
        Assert.IsTrue(await _db.PaperTags.AnyAsync(t => t.TagId == source.Id));
        Assert.AreEqual(1, await _db.QuestionTags.CountAsync(q => q.TagId == _tag));
        Assert.IsFalse((await _db.Tags.AsNoTracking().SingleAsync(t => t.Id == _tag)).IsFree);
    }

    [TestMethod]
    public async Task FailedProjectionRollsBackNewMembershipsNameAndAliases()
    {
        var collection = await Connect();
        await _db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_directory_write BEFORE DELETE ON PaperTags BEGIN SELECT RAISE(ABORT, 'test projection failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => _service.SaveAsync(collection.Id, collection.Revision, "Must roll back", 0, true, false, []));
        _db.ChangeTracker.Clear();
        Assert.AreEqual("Suzhou 21-26", (await _db.PaperCollections.SingleAsync()).Name);
        Assert.AreEqual("Suzhou 21-26", (await _db.Tags.SingleAsync(t => t.Id == _tag)).DisplayName);
        Assert.AreEqual(1, await _db.PaperCollectionPapers.CountAsync());
        Assert.AreEqual(1, await _db.PaperTags.CountAsync());
        Assert.IsNull(await _service.ResolveLegacyNameAsync("Must roll back"));
    }

    [TestMethod]
    public async Task StaleEditorCannotUndoAnotherEditorsMembershipChange()
    {
        var collection = await Connect();
        await _service.SetPaperCollectionsAsync(_paper, []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveAsync(collection.Id, collection.Revision, collection.Name, 0, true, false, [_paper]));
        Assert.AreEqual(0, await _db.PaperCollectionPapers.CountAsync());
        Assert.AreEqual(0, await _db.PaperTags.CountAsync());
    }

    [TestMethod]
    public async Task OldTagWritesCannotBypassDirectoryAndClassificationDoesNotPublishSourceTags()
    {
        var collection = await Connect();
        var tags = new TagService(_db, new StringLocalizer<TagService>(_localizers));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.DeleteTagAsync(_tag));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.RemoveTagFromPaperAsync(_paper, _tag));
        var tag = await _db.Tags.SingleAsync(t => t.Id == _tag); tag.DisplayName = "Bypass";
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.UpdateTagAsync(tag));
        _db.ChangeTracker.Clear();
        tag = await _db.Tags.SingleAsync(t => t.Id == _tag); tag.IsFree = true;
        await tags.UpdateTagAsync(tag);
        await tags.GetOrCreateTagAsync("2025 source", _taxonomy);
        Assert.AreEqual(1, (await _service.LegacyTagsAsync(_taxonomy))!.Count);
        await _service.SaveAsync(collection.Id, collection.Revision, "Renamed", 0, true, false, [_paper]);
        Assert.AreEqual(_tag, (await tags.GetOrCreateTagAsync("Suzhou 21-26")).Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tags.CreateTagAsync(new Tag { DisplayName = "Suzhou 21-26", NormalizedName = "SUZHOU 21-26" }));
    }

    [TestMethod]
    public async Task ArchiveHidesBothMenusAndRetainsEntitiesForRestoration()
    {
        var collection = await Connect();
        await _service.SaveAsync(collection.Id, collection.Revision, collection.Name, 4, true, true, [_paper]);
        Assert.AreEqual(0, (await _service.LegacyTagsAsync(_taxonomy))!.Count);
        Assert.AreEqual(1, await _db.Tags.CountAsync());
        Assert.AreEqual(1, await _db.PaperTags.CountAsync());
        Assert.AreEqual(1, await _db.PaperCollectionPapers.CountAsync());
        Assert.IsTrue((await _service.ResolveLegacyNameAsync(collection.Name))!.IsArchived);
        var latest = await _db.PaperCollections.AsNoTracking().SingleAsync();
        await _service.SaveAsync(latest.Id, latest.Revision, latest.Name, 4, true, false, [_paper]);
        Assert.AreEqual(1, (await _service.LegacyTagsAsync(_taxonomy))!.Count);
    }
}
