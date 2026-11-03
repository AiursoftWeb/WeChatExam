using System.Net;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Models.ContentSearchViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Tests.TestServices;
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
        var exact = NewQuestion("Boya");
        var related = NewQuestion("Who played Flowing Water?");
        related.QuestionCategory = exact.QuestionCategory;
        related.StandardAnswer = "Musician";

        var excluded = NewQuestion("Boya outside the selected period");
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya", Content = "A guqin musician" };
        var otherPoint = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Boya elsewhere", Content = "Another directory" };
        db.AddRange(category, other, exact, related, excluded, point, otherPoint);
        foreach (var directory in new[] { category, other })
            db.Add(new KnowledgeCategory { Id = directory.Id, Title = directory.Title, TotalCategoryId = directory.Id,
                TotalCategory = new TotalCategory { Id = directory.Id, Title = directory.Title } });
        db.Add(new KnowledgeCategoryKnowledgePoint { KnowledgeCategoryId = other.Id, KnowledgePointId = otherPoint.Id });
        db.Add(new KnowledgeCategoryKnowledgePoint { KnowledgeCategoryId = category.Id, KnowledgePointId = point.Id });
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
        var exact = NewQuestion("keyword-fixture");
        var related = NewQuestion("Who played Flowing Water?");
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
            .Select(_ => NewQuestion("paper-fixture")).ToArray();
        var keyword = NewQuestion("paper-fixture available");
        var related = NewQuestion("A related question");
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
    public async Task RecommendationsUsePeriodsAndExcludeSelfAssociatedAndStaleQuestions(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var other = new Category { Id = Guid.NewGuid(), Title = "Other", ParentId = null };
        var source = NewQuestion("Source question");
        var choices = Enumerable.Range(0, 8).Select(i => NewQuestion("Related question " + i)).ToArray();
        foreach (var choice in choices) { choice.QuestionCategory = source.QuestionCategory; }
        var wrongCategory = NewQuestion("Related outside the period");
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
            Id = point.Id, QuestionCategoryId = source.QuestionCategoryId, ExcludeIds = [choices[1].Id]
        }, true);
        Assert.IsTrue(available);
        Assert.HasCount(5, results);
        Assert.IsFalse(results.Any(r => r.Id == choices[0].Id || r.Id == choices[1].Id));
        var (_, similar) = await service.RecommendAsync(new RecommendationRequest { Id = source.Id, QuestionCategoryId = source.QuestionCategoryId }, false);
        Assert.HasCount(5, similar);
        Assert.IsFalse(similar.Any(r => r.Id == source.Id || r.Id == wrongCategory.Id));
        Assert.AreEqual(0, handler.Calls, "Saved source vectors avoid model requests when opening an editor.");

        // Cache refresh is deliberately not run: edits/deletion must still exclude stale entries.
        choices[2].Content = "Edited while the cache still contains the old vector";
        db.Remove(choices[3]);
        await db.SaveChangesAsync();
        var (_, fresh) = await service.RecommendAsync(new RecommendationRequest { Id = point.Id, QuestionCategoryId = source.QuestionCategoryId }, true);
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
        var request = new RecommendationRequest { Id = points[0].Id, ExcludeIds = [points[1].Id], QuestionCategoryId = Guid.NewGuid() };
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
    public async Task DraftSimilarityUsesUnsavedTextAndSelectedPeriodWithoutCreatingAQuestion(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var category = new Category { Id = Guid.NewGuid(), Title = "Music", ParentId = null };
        var otherCategory = new Category { Id = Guid.NewGuid(), Title = "Other", ParentId = null };
        var choices = Enumerable.Range(0, 7).Select(i => NewQuestion($"Candidate {i}")).ToArray();
        var period = choices[0].QuestionCategory;
        foreach (var choice in choices) { choice.QuestionCategory = period; }
        var other = NewQuestion("Outside selected period");
        db.AddRange(category, otherCategory, other); db.Questions.AddRange(choices); await db.SaveChangesAsync();
        foreach (var question in choices.Append(other)) SetEmbedding(question);
        await db.SaveChangesAsync();
        var handler = new QueryHandler(); var service = CreateSearch(db, handler);
        var request = new RecommendationRequest { Text = "Unsaved question and options", QuestionCategoryId = period!.Id, ExcludeIds = [choices[0].Id] };
        var (available, results) = await service.RecommendAsync(request, false);
        Assert.IsTrue(available); Assert.HasCount(5, results);
        Assert.IsTrue(results.All(q => !q.IsKnowledgePoint && q.Id != other.Id && q.Id != choices[0].Id));
        Assert.AreEqual(1, handler.Calls);
        await service.RecommendAsync(request, false);
        Assert.AreEqual(1, handler.Calls, "Repeated manual checks should reuse the query vector.");
        request.ExcludeIds = choices.Select(q => q.Id).ToArray();
        var (_, outsidePeriod) = await service.RecommendAsync(request, false);
        Assert.HasCount(0, outsidePeriod);
        request.QuestionCategoryId = null;
        var (_, allPeriods) = await service.RecommendAsync(request, false);
        Assert.AreEqual(other.Id, allPeriods.Single().Id);
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Cross-period knowledge", Content = "Topic" };
        db.Add(point); await db.SaveChangesAsync();
        request.Id = point.Id; request.QuestionCategoryId = period.Id;
        var (_, knowledgeRecommendations) = await service.RecommendAsync(request, true);
        Assert.AreEqual(other.Id, knowledgeRecommendations.Single().Id,
            "Knowledge-point recommendations must not mistake a directory for a question period.");
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
        var request = new RecommendationRequest { Text = "Unsaved knowledge title and content", ExcludeIds = [points[0].Id], QuestionCategoryId = Guid.NewGuid() };
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RelatedPracticeFiltersRightsBeforeRankingAndAllowsDifferentTypes(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var source = NewQuestion("The original mistake");
        var freePeriod = IntegrationTests.QuestionPeriodAssignmentTests.TestPeriod(true);
        freePeriod.TotalCategory = source.QuestionCategory!.TotalCategory;
        freePeriod.TotalCategoryId = source.QuestionCategory.TotalCategoryId;
        var paid = Enumerable.Range(0, 55).Select(i => NewQuestion("Paid candidate " + i)).ToArray();
        foreach (var q in paid) q.QuestionCategory = source.QuestionCategory;
        var choices = Enumerable.Range(0, 3).Select(i => NewQuestion("Free exercise " + i)).ToArray();
        foreach (var q in choices) q.QuestionCategory = freePeriod;
        choices[1].QuestionType = QuestionType.Blank;
        choices[2].QuestionType = QuestionType.Bool;
        var otherCourse = NewQuestion("Another course");
        otherCourse.QuestionCategory!.IsFree = true;
        var hidden = NewQuestion("Hidden high-ranking candidate");
        hidden.QuestionCategory!.TotalCategory = source.QuestionCategory.TotalCategory;
        hidden.QuestionCategory.TotalCategoryId = source.QuestionCategory.TotalCategoryId;
        hidden.QuestionCategory.IsFree = true; hidden.QuestionCategory.IsVisible = false;
        db.Add(hidden);
        db.AddRange(source, otherCourse); db.Questions.AddRange(paid); db.Questions.AddRange(choices);
        await db.SaveChangesAsync();
        SetEmbedding(source);
        foreach (var q in paid) SetRelatedVector(q, 0.95f, 1);
        for (var i = 0; i < choices.Length; i++) SetRelatedVector(choices[i], 0.8f, i + 2);
        SetRelatedVector(otherCourse, 0.97f, 9);
        SetRelatedVector(hidden, 0.97f, 10);
        await db.SaveChangesAsync();
        var handler = new QueryHandler();
        var cache = new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance);
        var service = CreateRelated(db, cache, handler);
        // Even an all-institution member has no additional independent course rights.
        var result = await service.FindAsync(Captured(source), new PaperAccessStatus { HasRealExamVip = true });
        Assert.IsTrue(result.Available);
        CollectionAssert.AreEquivalent(choices.Select(q => q.Id).ToArray(), result.Items.Select(q => q.Id).ToArray());
        Assert.IsTrue(result.Items.All(q => !q.SameQuestionCategory));
        foreach (var item in result.Items)
        {
            var question = choices.Single(q => q.Id == item.Id);
            Assert.AreEqual(question.Content, item.Content);
            Assert.AreEqual(freePeriod.Id, item.QuestionCategoryId);
            Assert.AreEqual(freePeriod.Title, item.QuestionCategoryTitle);
        }
        Assert.AreEqual(0, handler.Calls, "An unchanged source reuses its indexed vector.");
        var member = await service.FindAsync(Captured(source), new PaperAccessStatus
        {
            ActiveCategoryVips = [source.QuestionCategory.TotalCategoryId]
        });
        Assert.IsTrue(member.Items.Any(q => paid.Any(p => p.Id == q.Id)));
        Assert.IsTrue(member.Items[0].SameQuestionCategory);
        Assert.IsFalse(member.Items.Any(q => q.Id == hidden.Id));
        // Membership checks and entity freshness must not be cached with vector rankings.
        freePeriod.IsFree = false;
        await db.SaveChangesAsync();
        var revoked = await service.FindAsync(Captured(source), new PaperAccessStatus());
        Assert.HasCount(0, revoked.Items);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RelatedPracticeDeduplicatesLimitsLongAnswersAndRejectsStaleContent(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var source = NewQuestion("Source exercise"); source.QuestionCategory!.IsFree = true;
        var rows = new[] { "Source, exercise!", "Near duplicate", "Explain the event", "Discuss the event",
            "Identify the instrument", "Identify the instrument!", "Name the composer", "Unrelated" }
            .Select(NewQuestion).ToArray();
        foreach (var q in rows) q.QuestionCategory = source.QuestionCategory;
        rows[2].QuestionType = QuestionType.ShortAnswer;
        rows[3].QuestionType = QuestionType.Essay;
        rows[6].QuestionType = QuestionType.Blank;
        db.Add(source); db.Questions.AddRange(rows); await db.SaveChangesAsync();
        SetEmbedding(source);
        for (var i = 0; i < rows.Length; i++) SetRelatedVector(rows[i], 0.95f - i * 0.03f, i + 1);
        SetRelatedVector(rows[1], 0.995f, 2);
        SetRelatedVector(rows[7], 0.2f, 8);
        await db.SaveChangesAsync();
        var cache = new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance);
        var service = CreateRelated(db, cache, new QueryHandler());
        var result = await service.FindAsync(Captured(source), new PaperAccessStatus());
        CollectionAssert.AreEquivalent(new[] { rows[2].Id, rows[4].Id, rows[6].Id }, result.Items.Select(q => q.Id).ToArray());
        Assert.AreEqual(1, result.Items.Count(q => q.QuestionType is QuestionType.ShortAnswer or QuestionType.Essay));
        rows[2].Content = "Changed after the cached vector was loaded";
        db.Remove(rows[4]);
        var otherPeriod = IntegrationTests.QuestionPeriodAssignmentTests.TestPeriod(true);
        db.Add(otherPeriod);
        rows[6].QuestionCategory = otherPeriod;
        await db.SaveChangesAsync();
        var current = await service.FindAsync(Captured(source), new PaperAccessStatus());
        Assert.IsFalse(current.Items.Any(q => new[] { rows[2].Id, rows[4].Id, rows[6].Id }.Contains(q.Id)));
        Assert.IsTrue(current.Items.Count < 3, "Never pad with stale, unrelated or duplicate questions.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RelatedPracticeUsesCapturedContentAndHandlesUnavailableIndex(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var source = NewQuestion("Original topic"); source.QuestionCategory!.IsFree = true;
        var related = NewQuestion("Related topic"); related.QuestionCategory = source.QuestionCategory;
        db.AddRange(source, related); await db.SaveChangesAsync();
        var copy = Captured(source);
        var handler = new QueryHandler();
        var cache = new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance);
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        var service = CreateRelated(db, cache, handler, settings);
        var empty = await service.FindAsync(copy, new PaperAccessStatus());
        Assert.IsFalse(empty.Available); Assert.AreEqual(0, handler.Calls);
        SetEmbedding(source); SetRelatedVector(related, 0.8f, 1); await db.SaveChangesAsync();
        await cache.LoadAsync(db);
        source.Content = "A different topic after the mistake was captured";
        await db.SaveChangesAsync();
        var historical = await service.FindAsync(copy, new PaperAccessStatus());
        Assert.HasCount(1, historical.Items); Assert.AreEqual(1, handler.Calls);
        Assert.Contains("Original topic", handler.LastInput!);
        Assert.DoesNotContain("A different topic", handler.LastInput!);
        settings.EmbeddingEnabled = false;
        Assert.IsFalse((await service.FindAsync(copy, new PaperAccessStatus())).Available);
        Assert.AreEqual(1, handler.Calls);
        settings.EmbeddingEnabled = true; handler.Fail = true;
        copy = Captured(source, "Another historical topic that has no cached query");
        var failed = await service.FindAsync(copy, new PaperAccessStatus());
        Assert.IsFalse(failed.Available); Assert.HasCount(0, failed.Items);
        db.Remove(source); await db.SaveChangesAsync();
        Assert.HasCount(0, (await service.FindAsync(copy, new PaperAccessStatus())).Items);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReplacementExcludesPriorVersionsAndKeepsOneLongAnswer(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var source = NewQuestion("Original exercise"); source.QuestionCategory!.IsFree = true;
        var rows = new[] {"Current long answer", "Replaced question", "Replaced question!", "Another long answer", "Eligible choice"}
            .Select(NewQuestion).ToArray();
        foreach (var q in rows) q.QuestionCategory = source.QuestionCategory;
        rows[0].QuestionType = QuestionType.ShortAnswer; rows[3].QuestionType = QuestionType.Essay;
        db.Add(source); db.Questions.AddRange(rows); await db.SaveChangesAsync();
        SetEmbedding(source);
        for (var i = 0; i < rows.Length; i++) SetRelatedVector(rows[i], 0.9f - i * 0.03f, i + 1);
        await db.SaveChangesAsync();
        var current = new PracticeSessionItem {SourceQuestionId = rows[0].Id, Content = rows[0].Content,
            QuestionType = rows[0].QuestionType, Metadata = "{}", StandardAnswer = "A", Explanation = "B"};
        var replaced = new PracticeSessionItem {SourceQuestionId = rows[1].Id, Content = rows[1].Content,
            Metadata = "{}", StandardAnswer = "A", Explanation = "B", ReplacedAt = DateTime.UtcNow};
        var service = CreateRelated(db, new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance), new QueryHandler());
        var result = await service.FindReplacementAsync(Captured(source), new PaperAccessStatus(), [current, replaced], replaced.Id);
        Assert.HasCount(1, result.Items); Assert.AreEqual(rows[4].Id, result.Items[0].Id);
        var replacingLong = await service.FindReplacementAsync(Captured(source), new PaperAccessStatus(), [current, replaced], current.Id);
        Assert.HasCount(1, replacingLong.Items); Assert.AreEqual(rows[3].Id, replacingLong.Items[0].Id);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HiddenQuestionKnowledgeContextRequiresItsOwnAccessiblePlacement(bool relational)
    {
        await using var db = await CreateDatabase(relational);
        var question = NewQuestion("Hidden lesson exercise");
        question.QuestionCategory!.IsVisible = false; question.QuestionCategory.IsFree = true;
        var course = new TotalCategory { Title = "Lesson course" };
        var directory = new KnowledgeCategory { Title = "Paid lesson directory", TotalCategoryId = course.Id };
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Linked lesson", Content = "Lesson body" };
        db.AddRange(question, course, directory, lesson,
            new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = question.Id },
            new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgePointId = lesson.Id, KnowledgeCategoryId = directory.Id });
        await db.SaveChangesAsync();
        Assert.IsFalse(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, lesson.Id, new()));
        Assert.IsFalse(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, lesson.Id,
            new PaperAccessStatus { ActiveCategoryVips = [question.QuestionCategory.TotalCategoryId] }));
        var member = new PaperAccessStatus { ActiveCategoryVips = [course.Id] };
        Assert.IsTrue(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, lesson.Id, member));
        Assert.IsFalse(await QuestionAccess.HasKnowledgeContextAsync(db, Guid.NewGuid(), lesson.Id, member));
        Assert.IsFalse(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, Guid.NewGuid(), member));
        Assert.AreEqual(0, await db.Questions.Where(QuestionAccess.PracticeFilter(member)).CountAsync());
        directory.IsFree = true; await db.SaveChangesAsync();
        Assert.IsTrue(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, lesson.Id, new()));
        db.KnowledgeCategoryKnowledgePoints.RemoveRange(db.KnowledgeCategoryKnowledgePoints);
        await db.SaveChangesAsync();
        Assert.IsFalse(await QuestionAccess.HasKnowledgeContextAsync(db, question.Id, lesson.Id, member));
        db.ChangeTracker.Clear();
        Assert.IsFalse((await db.QuestionCategories.SingleAsync(c => c.Id == question.QuestionCategoryId)).IsVisible);
    }

    private static MistakeRecord Captured(Question q, string? content = null) => new()
    {
        UserId = "synthetic-owner", Content = content ?? q.Content, QuestionType = q.QuestionType,
        Metadata = q.Metadata, StandardAnswer = q.StandardAnswer, Explanation = q.Explanation,
        SourceQuestionId = q.Id, UserAnswer = "A wrong answer", GradingComment = string.Empty
    };

    private static void SetRelatedVector(Question q, float similarity, int axis)
    {
        var vector = new float[1024]; vector[0] = similarity; vector[axis] = MathF.Sqrt(1 - similarity * similarity);
        q.Embedding = EmbeddingHelper.Serialize(vector); q.LastEmbeddedAt = q.LastUpdatedAt;
    }

    private static RelatedQuestionService CreateRelated(WeChatExamDbContext db, ContentEmbeddingCache cache,
        QueryHandler handler, StubGlobalSettingsService? settings = null)
    {
        settings ??= new StubGlobalSettingsService { EmbeddingEnabled = true };
        var search = new ContentVectorSearchService(db, cache, settings,
            new EmbeddingService(settings, handler, NullLogger<EmbeddingService>.Instance),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<ContentVectorSearchService>.Instance);
        return new RelatedQuestionService(db, cache, settings, search);
    }

    private static ContentVectorSearchService CreateSearch(WeChatExamDbContext db, QueryHandler handler)
    {
        var settings = new StubGlobalSettingsService { EmbeddingEnabled = true };
        return new ContentVectorSearchService(db, new ContentEmbeddingCache(NullLogger<ContentEmbeddingCache>.Instance),
            settings, new EmbeddingService(settings, handler, NullLogger<EmbeddingService>.Instance),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<ContentVectorSearchService>.Instance);
    }

    private static Question NewQuestion(string content) => new()
    {
        QuestionCategory = IntegrationTests.QuestionPeriodAssignmentTests.TestPeriod(),
        Id = Guid.NewGuid(), Content = content, StandardAnswer = "Boya"
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
        public string? LastInput { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastInput = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Fail) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embeddings = new[] { EmbeddingHelper.Deserialize(Vector())! } })
            };
        }
    }
}
