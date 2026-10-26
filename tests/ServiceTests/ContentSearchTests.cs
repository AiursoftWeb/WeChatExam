using System.Net;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Models.ContentSearchViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Tests.TestServices;
using Aiursoft.WeChatExam.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class ContentSearchTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SearchScopesPeriodsAndKnowledgeDirectoriesIndependently(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var other = new Category { Id = Guid.NewGuid(), Title = "Other", ParentId = null };
        var exact = NewQuestion("Boya", category.Id);
        var related = NewQuestion("Who played Flowing Water?", other.Id);
        related.QuestionCategory = exact.QuestionCategory;
        related.StandardAnswer = "Musician";
        exact.CategoryId = null;
        var excluded = NewQuestion("Boya outside the selected period", category.Id);
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya", Content = "A guqin musician" };
        var otherPoint = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya elsewhere", Content = "Another directory" };
        db.AddRange(category, other, exact, related, excluded, point, otherPoint);
        db.Add(new CategoryKnowledgePoint { CategoryId = other.Id, KnowledgePointId = otherPoint.Id });
        db.Add(new CategoryKnowledgePoint { CategoryId = category.Id, KnowledgePointId = point.Id });
        await db.SaveChangesAsync();
        foreach (var q in new[] { exact, related, excluded }) SetEmbedding(q);
        point.Embedding = Vector(); point.LastEmbeddedAt = point.LastUpdatedAt;
        otherPoint.Embedding = Vector(); otherPoint.LastEmbeddedAt = otherPoint.LastUpdatedAt;
        await db.SaveChangesAsync();
        var handler = new QueryHandler();
        var service = CreateSearch(db, handler);

        var (results, usedAi) = await service.SearchAsync("Boya", "all", category.Id, questionCategoryId: exact.QuestionCategoryId);
        Assert.IsTrue(usedAi);
        Assert.HasCount(3, results);
        Assert.IsFalse(results.Any(r => r.Id == excluded.Id));
        Assert.AreEqual(related.Id, results[^1].Id, "Exact content/title matches must precede semantic-only matches.");
        Assert.IsTrue(results.Any(r => r.Id == point.Id && r.IsKnowledgePoint));
        Assert.IsTrue(results.Any(r => r.Id == exact.Id && !r.IsKnowledgePoint));
        Assert.AreEqual(exact.QuestionCategory!.TotalCategory!.Title + " · " + exact.QuestionCategory.Title,
            results.Single(r => r.Id == exact.Id).Category);
        var (keywords, _) = await service.SearchAsync("Boya", "all", category.Id, useAi: false,
            questionCategoryId: exact.QuestionCategoryId);
        CollectionAssert.AreEquivalent(new[] { exact.Id, point.Id }, keywords.Select(r => r.Id).ToArray());
        var (allQuestions, _) = await service.SearchAsync("Boya", "questions", category.Id);
        Assert.HasCount(3, allQuestions, "A knowledge directory must not restrict questions.");
        var (allPoints, _) = await service.SearchAsync("Boya", "knowledge", null,
            questionCategoryId: exact.QuestionCategoryId);
        Assert.HasCount(2, allPoints, "A period must not restrict knowledge points.");
        Assert.AreEqual(1, handler.Calls, "Repeated searches should reuse the query vector.");

        handler.Fail = true;
        var (fallback, fallbackUsedAi) = await service.SearchAsync("guqin", "knowledge", category.Id);
        Assert.IsFalse(fallbackUsedAi);
        Assert.HasCount(1, fallback);
        Assert.AreEqual(point.Id, fallback[0].Id);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KeywordModeSkipsModelAndCachedSemanticResults(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var exact = NewQuestion("keyword-fixture", category.Id);
        var related = NewQuestion("Who played Flowing Water?", category.Id);
        db.Add(category);
        db.Questions.AddRange(exact, related);
        await db.SaveChangesAsync();
        SetEmbedding(exact); SetEmbedding(related);
        await db.SaveChangesAsync();
        var handler = new QueryHandler();
        var service = CreateSearch(db, handler);

        var (keywords, usedAi) = await service.SearchAsync("keyword-fixture", "questions", null, useAi: false);
        Assert.IsFalse(usedAi);
        Assert.HasCount(1, keywords);
        Assert.AreEqual(exact.Id, keywords[0].Id);
        Assert.AreEqual(0, handler.Calls, "Keyword mode must not request a query embedding.");

        var (smart, smartUsedAi) = await service.SearchAsync("keyword-fixture", "questions", null);
        Assert.IsTrue(smartUsedAi);
        Assert.HasCount(2, smart);
        Assert.AreEqual(1, handler.Calls);
        var (cachedKeywords, cachedUsedAi) = await service.SearchAsync("keyword-fixture", "questions", null, useAi: false);
        Assert.IsFalse(cachedUsedAi);
        Assert.HasCount(1, cachedKeywords);
        Assert.AreEqual(exact.Id, cachedKeywords[0].Id);
        Assert.AreEqual(1, handler.Calls, "Keyword mode must ignore cached query vectors as well.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PaperSearchExcludesExistingQuestionsBeforeRankingAndLimits(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var paper = new Paper { Id = Guid.NewGuid(), Title = "Current paper" };
        var otherPaper = new Paper { Id = Guid.NewGuid(), Title = "Other paper" };
        var existing = Enumerable.Range(0, ContentVectorSearchService.MaxResults + 5)
            .Select(_ => NewQuestion("paper-fixture", category.Id)).ToArray();
        var keyword = NewQuestion("paper-fixture available", category.Id);
        var related = NewQuestion("A related question", category.Id);
        related.QuestionType = QuestionType.Blank;
        db.AddRange(category, paper, otherPaper, keyword, related);
        db.Questions.AddRange(existing);
        await db.SaveChangesAsync();
        foreach (var q in existing)
            db.PaperQuestions.Add(new PaperQuestion { PaperId = paper.Id, QuestionId = q.Id, Order = 1, Score = 10 });
        db.PaperQuestions.Add(new PaperQuestion { PaperId = otherPaper.Id, QuestionId = keyword.Id, Order = 1, Score = 10 });
        foreach (var q in existing.Concat([keyword, related])) SetEmbedding(q);
        await db.SaveChangesAsync();
        var handler = new QueryHandler();
        var service = CreateSearch(db, handler);

        var (keywords, keywordUsedAi) = await service.SearchAsync("paper-fixture", "questions", null,
            useAi: false, excludePaperId: paper.Id);
        Assert.IsFalse(keywordUsedAi);
        Assert.HasCount(1, keywords);
        Assert.AreEqual(keyword.Id, keywords[0].Id, "Exclude paper members before taking the top keyword matches.");
        Assert.AreEqual(0, handler.Calls);
        var (smart, usedAi) = await service.SearchAsync("paper-fixture", "questions", null, excludePaperId: paper.Id);
        Assert.IsTrue(usedAi);
        Assert.HasCount(2, smart);
        Assert.IsTrue(smart.Any(q => q.Id == related.Id));
        Assert.IsFalse(smart.Any(q => existing.Any(e => e.Id == q.Id)), "Semantic results must also exclude paper members.");
        var (filtered, _) = await service.SearchAsync("paper-fixture", "questions", null,
            QuestionType.Choice, excludePaperId: paper.Id);
        Assert.HasCount(1, filtered);
        Assert.AreEqual(keyword.Id, filtered[0].Id);
        Assert.AreEqual(1, handler.Calls, "Changing the paper/type filter can reuse the query vector.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RecommendationsReturnFiveAndExcludeSelfAssociatedAndOtherCategoryQuestions(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var other = new Category { Id = Guid.NewGuid(), Title = "Other", ParentId = null };
        var source = NewQuestion("Source question", category.Id);
        var choices = Enumerable.Range(0, 8).Select(i => NewQuestion("Related question " + i, category.Id)).ToArray();
        var wrongCategory = NewQuestion("Related outside the category", other.Id);
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Guqin", Content = "Boya" };
        db.AddRange(category, other, point, source, wrongCategory);
        db.Questions.AddRange(choices);
        db.Add(new KnowledgePointQuestion { KnowledgePointId = point.Id, QuestionId = choices[0].Id });
        await db.SaveChangesAsync();
        foreach (var question in choices.Concat([source, wrongCategory])) SetEmbedding(question);
        point.Embedding = Vector(); point.LastEmbeddedAt = point.LastUpdatedAt;
        await db.SaveChangesAsync();
        var handler = new QueryHandler();
        var service = CreateSearch(db, handler);

        var (available, results) = await service.RecommendAsync(new RecommendationRequest
        {
            Id = point.Id, CategoryIds = [category.Id], ExcludeIds = [choices[1].Id]
        }, true);
        Assert.IsTrue(available);
        Assert.HasCount(5, results);
        Assert.IsFalse(results.Any(r => r.Id == choices[0].Id || r.Id == choices[1].Id || r.Id == wrongCategory.Id));
        var (_, similar) = await service.RecommendAsync(new RecommendationRequest { Id = source.Id, CategoryIds = [category.Id] }, false);
        Assert.HasCount(5, similar);
        Assert.IsFalse(similar.Any(r => r.Id == source.Id || r.Id == wrongCategory.Id));
        Assert.AreEqual(0, handler.Calls, "Saved source vectors avoid model requests when opening an editor.");

        // Cache refresh is deliberately not run: edits/deletion must still exclude stale entries.
        choices[2].Content = "Edited while the cache still contains the old vector";
        db.Remove(choices[3]);
        await db.SaveChangesAsync();
        var (_, fresh) = await service.RecommendAsync(new RecommendationRequest { Id = point.Id, CategoryIds = [category.Id] }, true);
        Assert.IsFalse(fresh.Any(r => r.Id == choices[2].Id || r.Id == choices[3].Id));
        await service.RecommendAsync(new RecommendationRequest { Id = source.Id, Text = "Unsaved new question content" }, false);
        Assert.AreEqual(1, handler.Calls, "Explicit editor refresh can use unsaved content without saving it.");
        db.ChangeTracker.Clear();
        Assert.AreEqual("Source question", (await db.Questions.SingleAsync(q => q.Id == source.Id)).Content);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SimilarKnowledgePointsExcludeSelfStaleDeletedAndExplicitExclusions(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var points = Enumerable.Range(0, 10).Select(i => new KnowledgePoint
        {
            Id = Guid.NewGuid(), Title = $"Point {i}", Content = "Related knowledge"
        }).ToArray();
        db.KnowledgePoints.AddRange(points); await db.SaveChangesAsync();
        foreach (var point in points) { point.Embedding = Vector(); point.LastEmbeddedAt = point.LastUpdatedAt; }
        await db.SaveChangesAsync();
        var handler = new QueryHandler(); var service = CreateSearch(db, handler);
        var request = new RecommendationRequest { Id = points[0].Id, ExcludeIds = [points[1].Id], CategoryIds = [Guid.NewGuid()] };
        var (available, initial) = await service.SimilarKnowledgePointsAsync(request);
        Assert.IsTrue(available); Assert.HasCount(5, initial);
        Assert.IsTrue(initial.All(k => k.IsKnowledgePoint && k.Id != points[0].Id && k.Id != points[1].Id));
        Assert.AreEqual(0, handler.Calls, "Opening an editor should reuse the current saved vector.");
        var stale = points.Single(p => p.Id == initial[0].Id);
        var deleted = points.Single(p => p.Id == initial[1].Id);
        stale.Content = "Revised while the cache still holds the old vector";
        db.Remove(deleted); await db.SaveChangesAsync();
        var (_, current) = await service.SimilarKnowledgePointsAsync(request);
        Assert.HasCount(5, current);
        Assert.IsFalse(current.Any(k => k.Id == stale.Id || k.Id == deleted.Id));
        request.Text = "Unsaved draft knowledge"; request.Refresh = true;
        var (draftAvailable, draft) = await service.SimilarKnowledgePointsAsync(request);
        Assert.IsTrue(draftAvailable); Assert.HasCount(5, draft); Assert.AreEqual(1, handler.Calls);
        Assert.AreEqual("Related knowledge", (await db.KnowledgePoints.SingleAsync(k => k.Id == points[0].Id)).Content);
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync());
        request.Text = "Unavailable draft"; handler.Fail = true;
        var (failed, empty) = await service.SimilarKnowledgePointsAsync(request);
        Assert.IsFalse(failed); Assert.HasCount(0, empty);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DraftSimilarityUsesUnsavedTextAndSelectedCategoryWithoutCreatingAQuestion(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var otherCategory = new Category { Id = Guid.NewGuid(), Title = "Other", ParentId = null };
        var choices = Enumerable.Range(0, 7).Select(i => NewQuestion($"Candidate {i}", category.Id)).ToArray();
        var other = NewQuestion("Outside selected category", otherCategory.Id);
        db.AddRange(category, otherCategory, other); db.Questions.AddRange(choices); await db.SaveChangesAsync();
        foreach (var question in choices.Append(other)) SetEmbedding(question);
        await db.SaveChangesAsync();
        var handler = new QueryHandler(); var service = CreateSearch(db, handler);
        var request = new RecommendationRequest { Text = "Unsaved question and options", CategoryIds = [category.Id], ExcludeIds = [choices[0].Id] };
        var (available, results) = await service.RecommendAsync(request, false);
        Assert.IsTrue(available); Assert.HasCount(5, results);
        Assert.IsTrue(results.All(q => !q.IsKnowledgePoint && q.Id != other.Id && q.Id != choices[0].Id));
        Assert.AreEqual(1, handler.Calls);
        await service.RecommendAsync(request, false);
        Assert.AreEqual(1, handler.Calls, "Repeated manual checks should reuse the query vector.");
        request.Text = "Unavailable draft query"; handler.Fail = true;
        var (failed, empty) = await service.RecommendAsync(request, false);
        Assert.IsFalse(failed); Assert.HasCount(0, empty);
        Assert.AreEqual(8, await db.Questions.CountAsync());
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync());
        Assert.IsFalse(await db.Questions.AnyAsync(q => q.Id == Guid.Empty));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task KnowledgeDraftSimilarityUsesUnsavedTextWithoutCreatingContentOrRelationships(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var points = Enumerable.Range(0, 8).Select(i => new KnowledgePoint
        {
            Id = Guid.NewGuid(), Title = $"Knowledge candidate {i}", Content = "Related topic"
        }).ToArray();
        db.KnowledgePoints.AddRange(points); await db.SaveChangesAsync();
        foreach (var point in points) { point.Embedding = Vector(); point.LastEmbeddedAt = point.LastUpdatedAt; }
        await db.SaveChangesAsync();
        var handler = new QueryHandler(); var service = CreateSearch(db, handler);
        var request = new RecommendationRequest { Text = "Unsaved knowledge title and content", ExcludeIds = [points[0].Id], CategoryIds = [Guid.NewGuid()] };
        var (available, results) = await service.SimilarKnowledgePointsAsync(request);
        Assert.IsTrue(available); Assert.HasCount(5, results);
        Assert.IsTrue(results.All(k => k.IsKnowledgePoint && k.Id != points[0].Id));
        Assert.AreEqual(1, handler.Calls);
        await service.SimilarKnowledgePointsAsync(request);
        Assert.AreEqual(1, handler.Calls, "Repeated checks should reuse the draft vector.");
        request.Text = "Unavailable knowledge draft"; handler.Fail = true;
        var (failed, empty) = await service.SimilarKnowledgePointsAsync(request);
        Assert.IsFalse(failed); Assert.HasCount(0, empty);
        Assert.AreEqual(8, await db.KnowledgePoints.CountAsync());
        Assert.IsFalse(await db.KnowledgePoints.AnyAsync(k => k.Id == Guid.Empty));
        Assert.AreEqual(0, await db.KnowledgePointQuestions.CountAsync());
    }

    private static ContentVectorSearchService CreateSearch(WeChatExamDbContext db, QueryHandler handler)
    {
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        return new ContentVectorSearchService(db, new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance),
            settings, new EmbeddingService(settings, handler, NullLogger<EmbeddingService>.Instance),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<ContentVectorSearchService>.Instance);
    }

    private static Question NewQuestion(string content, Guid categoryId) => new()
    {
        QuestionCategory = IntegrationTests.QuestionPeriodAssignmentTests.TestPeriod(),
        Id = Guid.NewGuid(), Content = content, StandardAnswer = "Boya", CategoryId = categoryId
    };

    private static byte[] Vector()
    {
        var vector = new float[1024]; vector[0] = 1;
        return EmbeddingHelper.Serialize(vector);
    }

    private static void SetEmbedding(Question question)
    {
        question.Embedding = Vector(); question.LastEmbeddedAt = question.LastUpdatedAt;
    }

    private static async Task<WeChatExamDbContext> CreateDatabase(bool relational)
    {
        if (!relational) return new InMemoryContext(new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite("DataSource=:memory:").Options);
        await db.Database.OpenConnectionAsync(); await db.Database.MigrateAsync(); return db;
    }

    private sealed class QueryHandler : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embeddings = new[] { EmbeddingHelper.Deserialize(Vector())! } })
            });
        }
    }
}
