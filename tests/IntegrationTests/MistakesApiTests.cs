using System.Net;
using System.Net.Http.Headers;
using System.Data.Common;
using System.Security.Claims;
using Aiursoft.DbTools;
using Aiursoft.WebTools;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Controllers.MiniProgramApi;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Mvc;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class MistakesApiTests : TestBase
{
    private readonly StubWeChatService _wechat = new();
    private readonly StubOllamaService _ollama = new();
    private SqliteConnection? _keeper;
    private string? _storage;

    [TestInitialize]
    public override async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockWeChatService = _wechat;
        TestStartupWithMockWeChat.MockWeChatPayService = new StubWeChatPayService();
        TestStartupWithMockWeChat.MockDistributionChannelService = new StubDistributionChannelService();
        TestStartupWithMockWeChat.MockOllamaService = _ollama;
        TestStartupWithMockWeChat.MockVipProductService = null;
        _keeper = new SqliteConnection($"Data Source=mistakes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True");
        await _keeper.OpenAsync();
        TestStartupWithMockWeChat.SqliteConnectionString = _keeper.ConnectionString;
        _storage = Directory.CreateTempSubdirectory("MistakesApi-").FullName;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        try
        {
            Server = await AppAsync<TestStartupWithMockWeChat>([$"Storage:Path={_storage}"], port: reservation.Port);
        }
        finally
        {
            TestStartupWithMockWeChat.SqliteConnectionString = null;
        }
        reservation.ConfigureServer(Server);
        await Server.UpdateDbAsync<WeChatExamDbContext>();
        await Server.StartAsync();
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        try { await base.CleanServer(); }
        finally
        {
            if (_keeper != null) await _keeper.DisposeAsync();
            if (_storage != null && Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
            Http.Dispose();
        }
    }

    private async Task<string> Login(string openId)
    {
        _wechat.DefaultSession = new WeChatSessionResult { IsSuccess = true, OpenId = openId, SessionKey = "synthetic-session" };
        var response = await Http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = "synthetic-code" });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenDto>())!.Token;
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var scope = Server!.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Users
            .Where(u => u.MiniProgramOpenId == openId).Select(u => u.Id).SingleAsync();
    }

    private static MistakeRecord Copy(string userId, Guid? questionId = null, Guid? snapshotId = null,
        MistakeSource source = MistakeSource.QuestionBank, int order = 1, DateTime? time = null) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{order:D12}"), UserId = userId,
        SourceQuestionId = questionId, SourceQuestionSnapshotId = snapshotId, Source = source,
        Content = "Historical question " + order, Metadata = "{\"options\":[\"A\",\"B\"]}", QuestionType = QuestionType.Choice,
        UserAnswer = "Wrong answer " + order, StandardAnswer = "Historical reference " + order,
        Explanation = "Historical explanation " + order, Score = 3, MaxScore = 10,
        GradingComment = "Historical feedback " + order,
        SourcePaperTitle = source == MistakeSource.QuestionBank ? null : "Historical paper",
        SourcePaperId = source == MistakeSource.QuestionBank ? null : Guid.NewGuid(),
        SourcePaperSnapshotId = source == MistakeSource.QuestionBank ? null : Guid.NewGuid(),
        SourcePaperVersion = source == MistakeSource.QuestionBank ? null : order,
        CreationTime = time ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(order)
    };

    private async Task Seed(params MistakeRecord[] records)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.MistakeRecords.AddRange(records);
        await db.SaveChangesAsync();
    }

    [TestMethod]
    public async Task AllRoutesRequireBearerEvenWithAWebLogin()
    {
        async Task AssertUnauthorized()
        {
            foreach (var url in new[] { "/api/Mistakes", $"/api/Mistakes/{Guid.NewGuid()}", $"/api/Mistakes/{Guid.NewGuid()}/attempts", $"/api/Mistakes/{Guid.NewGuid()}/related" })
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync(url)).StatusCode);
            foreach (var url in new[] { "/api/Mistakes", $"/api/Mistakes/{Guid.NewGuid()}" })
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.DeleteAsync(url)).StatusCode);
        }
        await AssertUnauthorized();
        await RegisterAndLoginAsync();
        await AssertUnauthorized();
    }

    private async Task<(Guid MistakeId, Question[] Questions)> SeedPractice(string owner, int count = 3)
    {
        var copy = Copy(owner, Guid.NewGuid());
        await Seed(copy);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
        await settings.UpdateSettingAsync(SettingsMap.EnableEmbeddingBasedSearch, "true");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingOllamaInstance, "https://embedding.example.invalid");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingModel, "bge-m3:latest");
        var period = QuestionPeriodAssignmentTests.TestPeriod(true);
        var source = new Question { Id = copy.SourceQuestionId!.Value, QuestionCategory = period,
            Content = copy.Content, QuestionType = copy.QuestionType, Metadata = copy.Metadata,
            StandardAnswer = copy.StandardAnswer, Explanation = copy.Explanation };
        var questions = Enumerable.Range(0, count).Select(i => new Question
        {
            Id = Guid.NewGuid(), QuestionCategory = period, Content = "Related practice " + i,
            Metadata = "{\"options\":[\"A\",\"B\"]}", StandardAnswer = "ANSWER" + i,
            Explanation = "PRIVATE EXPLANATION " + i,
            QuestionType = i == 2 ? QuestionType.ShortAnswer : QuestionType.Choice,
            GradingStrategy = i == 2 ? GradingStrategy.AiEval : GradingStrategy.ExactMatch
        }).ToArray();
        db.Add(source); db.Questions.AddRange(questions); await db.SaveChangesAsync();
        var seed = new float[1024]; seed[0] = 1;
        source.Embedding = EmbeddingHelper.Serialize(seed); source.LastEmbeddedAt = source.LastUpdatedAt;
        for (var i = 0; i < questions.Length; i++)
        {
            var vector = new float[1024]; vector[0] = 0.8f; vector[i + 1] = 0.6f;
            questions[i].Embedding = EmbeddingHelper.Serialize(vector);
            questions[i].LastEmbeddedAt = questions[i].LastUpdatedAt;
        }
        await db.SaveChangesAsync();
        return (copy.Id, questions);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task PracticeAnswersSupportRetryingTransactionsWithoutDuplicatingHistory(int failurePoint)
    {
        var failure = new AnswerSaveFailure();
        var commitFailure = new AnswerCommitFailure();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("Data Source=:memory:")
            .ReplaceService<IExecutionStrategyFactory, AnswerRetryStrategyFactory>()
            .AddInterceptors(failure, commitFailure).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var owner = new User { UserName = "retry-owner", DisplayName = "Retry owner" };
        var question = new Question { Id = Guid.NewGuid(), Content = "Fixed question", StandardAnswer = "Right",
            Explanation = "Fixed explanation", QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true) };
        var session = new PracticeSession { Id = Guid.NewGuid(), UserId = owner.Id, SourceMistakeId = Guid.NewGuid() };
        var item = new PracticeSessionItem { SessionId = session.Id, SourceQuestionId = question.Id,
            Content = question.Content, Metadata = "{}", StandardAnswer = question.StandardAnswer, Explanation = question.Explanation };
        session.Items.Add(item);
        db.AddRange(owner, question, session);
        await db.SaveChangesAsync();
        failure.Armed = failurePoint == 1;
        commitFailure.Armed = failurePoint == 2;
        using var scope = Server!.Services.CreateScope();
        var controller = new PracticeSessionsController(db, null!,
            scope.ServiceProvider.GetRequiredService<IPaperAccessService>(),
            scope.ServiceProvider.GetRequiredService<IGradingService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner.Id)], "Bearer"))
            } }
        };
        var request = new PracticeSessionAnswerDto { UserAnswer = "Wrong" };
        var response = (OkObjectResult)await controller.Answer(session.Id, item.Id, request, default);
        var result = (PracticeSessionDto)response.Value!;
        Assert.IsTrue(result.Completed);
        Assert.AreEqual(1, result.AnsweredCount);
        Assert.AreEqual("Wrong", result.Items.Single().UserAnswer);
        Assert.AreEqual("Right", result.Items.Single().StandardAnswer);
        Assert.AreEqual(false, result.Items.Single().IsCorrect);
        Assert.IsFalse(failure.Armed);
        Assert.IsFalse(commitFailure.Armed);
        Assert.IsInstanceOfType<OkObjectResult>(await controller.Answer(session.Id, item.Id, request, default));
        Assert.IsInstanceOfType<ConflictResult>(await controller.Answer(session.Id, item.Id,
            new PracticeSessionAnswerDto { UserAnswer = "Changed" }, default));
        Assert.AreEqual(1, await db.UserPracticeHistories.CountAsync());
        Assert.AreEqual(1, await db.MistakeRecords.CountAsync());
        Assert.AreEqual(1, await db.PracticeSessionItems.CountAsync(i => i.SubmittedAt != null));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public async Task PracticeReplacementsSupportRetryingTransactionsWithoutReplacingTwice(int failurePoint)
    {
        var owner = await Login("replacement-retry-owner");
        var (mistakeId, _) = await SeedPractice(owner, 4);
        var failure = new AnswerSaveFailure();
        var commitFailure = new AnswerCommitFailure();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite(_keeper!.ConnectionString)
            .ReplaceService<IExecutionStrategyFactory, AnswerRetryStrategyFactory>()
            .AddInterceptors(failure, commitFailure).Options);
        using var scope = Server!.Services.CreateScope();
        var controller = new PracticeSessionsController(db,
            new RelatedQuestionService(db, scope.ServiceProvider.GetRequiredService<ContentEmbeddingCache>(),
                scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>(),
                scope.ServiceProvider.GetRequiredService<ContentVectorSearchService>()),
            scope.ServiceProvider.GetRequiredService<IPaperAccessService>(),
            scope.ServiceProvider.GetRequiredService<IGradingService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "Bearer"))
            } }
        };
        var id = Guid.NewGuid();
        var started = (ObjectResult)await controller.Start(id, new StartPracticeSessionDto { MistakeId = mistakeId }, default);
        var initial = (PracticeSessionDto)started.Value!;
        var original = initial.Items.First();
        failure.Armed = failurePoint == 1;
        commitFailure.Armed = failurePoint == 2;
        var request = new ReplacePracticeSessionItemDto { Reason = PracticeReplacementReason.Familiar };
        var response = (OkObjectResult)await controller.Replace(id, original.Id, request, default);
        var result = (PracticeSessionDto)response.Value!;
        Assert.AreEqual(3, result.Items.Count);
        Assert.AreEqual(0, result.AnsweredCount);
        Assert.IsFalse(result.Completed);
        Assert.IsFalse(result.Items.Any(i => i.Id == original.Id));
        Assert.AreEqual(2, result.Items.Count(i => initial.Items.Any(old => old.Id == i.Id)));
        Assert.IsFalse(failure.Armed);
        Assert.IsFalse(commitFailure.Armed);
        var replay = (OkObjectResult)await controller.Replace(id, original.Id, request, default);
        CollectionAssert.AreEqual(result.Items.Select(i => i.Id).ToArray(),
            ((PracticeSessionDto)replay.Value!).Items.Select(i => i.Id).ToArray());
        Assert.IsInstanceOfType<ConflictResult>(await controller.Replace(id, original.Id,
            new ReplacePracticeSessionItemDto { Reason = PracticeReplacementReason.NotRelated }, default));
        Assert.AreEqual(1, await db.PracticeSessionItems.CountAsync(i => i.ReplacedAt != null));
        Assert.AreEqual(4, await db.PracticeSessionItems.CountAsync());
        Assert.AreEqual(1, (await db.PracticeSessions.AsNoTracking().SingleAsync()).ReplacementVersion);
        Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
        Assert.AreEqual(1, await db.MistakeRecords.CountAsync());
    }

    private sealed class AnswerRetryStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new AnswerRetryStrategy(dependencies);
    }

    private sealed class AnswerRetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TimeoutException;
    }

    private sealed class AnswerSaveFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Armed = false;
            throw new TimeoutException("Synthetic failure after saving answer history, before commit.");
        }
    }

    private sealed class AnswerCommitFailure : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return Task.CompletedTask;
            Armed = false;
            throw new TimeoutException("Synthetic lost acknowledgement after answer commit.");
        }
    }

    [TestMethod]
    public async Task PracticeSessionsKeepFixedCopiesAndCommitEachAnswerOnce()
    {
        var owner = await Login("session-owner");
        var (mistakeId, questions) = await SeedPractice(owner);
        var id = Guid.NewGuid();
        var url = $"/api/PracticeSessions/{id}";
        var starts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Http.PutAsJsonAsync(url, new StartPracticeSessionDto { MistakeId = mistakeId })));
        Assert.AreEqual(1, starts.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(starts.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        var created = starts.Single(r => r.StatusCode == HttpStatusCode.Created);
        Assert.IsTrue(created.Headers.CacheControl!.NoStore);
        var initial = (await created.Content.ReadFromJsonAsync<PracticeSessionDto>())!;
        Assert.AreEqual(id, initial.Id); Assert.AreEqual(mistakeId, initial.SourceMistakeId);
        Assert.IsTrue(initial.CreationTime > DateTime.UtcNow.AddMinutes(-1));
        Assert.AreEqual(0, initial.AnsweredCount); Assert.IsFalse(initial.Completed);
        Assert.HasCount(3, initial.Items);
        Assert.IsTrue(initial.Items.All(i => i.StandardAnswer == null && i.Explanation == null && i.UserAnswer == null));
        foreach (var item in initial.Items)
        {
            var q = questions.Single(q => q.Id == item.SourceQuestionId);
            Assert.AreEqual(q.Content, item.Content); Assert.AreEqual(q.Metadata, item.Metadata);
            Assert.AreEqual(q.QuestionType, item.QuestionType); Assert.AreEqual("Ready", item.Availability);
        }
        CollectionAssert.AreEqual(new[] {0, 1, 2}, initial.Items.Select(i => i.Position).ToArray());
        var repeated = await Http.PutAsJsonAsync(url, new StartPracticeSessionDto { MistakeId = mistakeId });
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);
        CollectionAssert.AreEqual(initial.Items.Select(i => i.Id).ToArray(),
            (await repeated.Content.ReadFromJsonAsync<PracticeSessionDto>())!.Items.Select(i => i.Id).ToArray());
        var first = initial.Items.Single(i => i.SourceQuestionId == questions[0].Id);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var live = await db.Questions.SingleAsync(q => q.Id == questions[0].Id);
            live.Content = "Later edited content"; live.StandardAnswer = "Later edited answer";
            await db.SaveChangesAsync();
        }
        var answerUrl = url + $"/items/{first.Id}/answer";
        var attempts = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            Http.PostAsJsonAsync(answerUrl, new PracticeSessionAnswerDto {UserAnswer = "wrong"})));
        foreach (var response in attempts) Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var answered = (await attempts[0].Content.ReadFromJsonAsync<PracticeSessionDto>())!;
        var result = answered.Items.Single(i => i.Id == first.Id);
        Assert.AreEqual(1, answered.AnsweredCount); Assert.AreEqual("Answered", result.Availability);
        Assert.AreEqual(questions[0].Content, result.Content); Assert.AreEqual("ANSWER0", result.StandardAnswer);
        Assert.AreEqual(questions[0].Explanation, result.Explanation); Assert.AreEqual("wrong", result.UserAnswer);
        Assert.AreEqual(false, result.IsCorrect); Assert.AreEqual(0, result.Score); Assert.IsNotNull(result.SubmittedAt);
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.PostAsJsonAsync(answerUrl,
            new PracticeSessionAnswerDto { UserAnswer = "ANSWER0" })).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(1, await db.PracticeSessions.CountAsync());
            Assert.AreEqual(1, await db.UserPracticeHistories.CountAsync());
            Assert.AreEqual(2, await db.MistakeRecords.CountAsync());
            var captured = await db.MistakeRecords.SingleAsync(m => m.Id != mistakeId);
            Assert.AreEqual(questions[0].Content, captured.Content); Assert.AreEqual("ANSWER0", captured.StandardAnswer);
        }
        var second = initial.Items.Single(i => i.SourceQuestionId == questions[1].Id);
        (await Http.PostAsJsonAsync(url + $"/items/{second.Id}/answer",
            new PracticeSessionAnswerDto { UserAnswer = "ANSWER1" })).EnsureSuccessStatusCode();
        var last = initial.Items.Single(i => i.SourceQuestionId == questions[2].Id);
        _ollama.Failure = new HttpRequestException("Synthetic grading outage");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Http.PostAsJsonAsync(url + $"/items/{last.Id}/answer",
            new PracticeSessionAnswerDto { UserAnswer = "My explanation" })).StatusCode);
        var pending = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        Assert.AreEqual(2, pending.AnsweredCount); Assert.IsNull(pending.Items.Single(i => i.Id == last.Id).SubmittedAt);
        _ollama.Failure = null;
        _ollama.Response = "{\"Score\":7,\"IsCorrect\":true,\"Comment\":\"Core correct; supporting detail missing.\"}";
        var completedResponse = await Http.PostAsJsonAsync(url + $"/items/{last.Id}/answer",
            new PracticeSessionAnswerDto { UserAnswer = "My explanation" });
        completedResponse.EnsureSuccessStatusCode();
        var completed = (await completedResponse.Content.ReadFromJsonAsync<PracticeSessionDto>())!;
        Assert.IsTrue(completed.Completed); Assert.AreEqual(3, completed.AnsweredCount);
        Assert.AreEqual(7, completed.Items.Single(i => i.Id == last.Id).Score);
        Assert.AreEqual("Core correct; supporting detail missing.", completed.Items.Single(i => i.Id == last.Id).GradingComment);
        var calls = _ollama.Questions.Count;
        (await Http.PostAsJsonAsync(url + $"/items/{last.Id}/answer",
            new PracticeSessionAnswerDto { UserAnswer = "My explanation" })).EnsureSuccessStatusCode();
        Assert.AreEqual(calls, _ollama.Questions.Count, "A committed AI answer must never be graded again.");
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(3, await db.UserPracticeHistories.CountAsync());
            Assert.AreEqual(2, await db.MistakeRecords.CountAsync());
            db.Questions.Remove(await db.Questions.SingleAsync(q => q.Id == questions[0].Id));
            db.MistakeRecords.Remove(await db.MistakeRecords.SingleAsync(m => m.Id == mistakeId));
            await db.SaveChangesAsync();
        }
        var retained = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        Assert.IsTrue(retained.Completed); Assert.AreEqual("ANSWER0", retained.Items.Single(i => i.Id == first.Id).StandardAnswer);
    }

    [TestMethod]
    public async Task ReplacingPracticeItemsIsIdempotentAndNeverCreatesAnswerHistory()
    {
        var owner = await Login("replace-owner");
        var (mistakeId, _) = await SeedPractice(owner, 6);
        var url = $"/api/PracticeSessions/{Guid.NewGuid()}";
        (await Http.PutAsJsonAsync(url, new StartPracticeSessionDto {MistakeId = mistakeId})).EnsureSuccessStatusCode();
        var initial = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        var item = initial.Items[0];
        var replaceUrl = url + $"/items/{item.Id}/replace";
        var request = new ReplacePracticeSessionItemDto {Reason = PracticeReplacementReason.Familiar};
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Http.PostAsJsonAsync(replaceUrl, request)));
        Assert.IsTrue(responses.All(r => r.StatusCode == HttpStatusCode.OK));
        var current = (await responses[0].Content.ReadFromJsonAsync<PracticeSessionDto>())!;
        Assert.HasCount(3, current.Items); Assert.AreEqual(0, current.AnsweredCount); Assert.IsFalse(current.Completed);
        Assert.IsFalse(current.Items.Any(i => i.Id == item.Id || i.SourceQuestionId == item.SourceQuestionId));
        var replacement = current.Items.Single(i => i.Position == item.Position);
        CollectionAssert.AreEquivalent(initial.Items.Where(i => i.Id != item.Id).Select(i => i.Id).ToArray(),
            current.Items.Where(i => i.Id != replacement.Id).Select(i => i.Id).ToArray());
        Assert.IsNull(replacement.StandardAnswer); Assert.IsNull(replacement.Explanation);
        Assert.IsNull(replacement.SubmittedAt); Assert.IsNull(replacement.Score);
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.PostAsJsonAsync(replaceUrl,
            new ReplacePracticeSessionItemDto {Reason = PracticeReplacementReason.NotRelated})).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "A"})).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var old = await db.PracticeSessionItems.SingleAsync(i => i.Id == item.Id);
            Assert.IsNotNull(old.ReplacedAt); Assert.AreEqual(request.Reason, old.ReplacementReason);
            Assert.IsNull(old.SubmittedAt); Assert.IsNull(old.IsCorrect); Assert.IsNull(old.UserAnswer);
            Assert.AreEqual(4, await db.PracticeSessionItems.CountAsync());
            Assert.AreEqual(1, (await db.PracticeSessions.SingleAsync()).ReplacementVersion);
            Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
            Assert.AreEqual(1, await db.MistakeRecords.CountAsync());
        }
        // Repeated replacements exclude the complete history, not just the three visible items.
        var seen = initial.Items.Select(i => i.SourceQuestionId).Append(replacement.SourceQuestionId).ToHashSet();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await Http.PostAsJsonAsync(url + $"/items/{replacement.Id}/replace", request);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity) break;
            response.EnsureSuccessStatusCode();
            current = (await response.Content.ReadFromJsonAsync<PracticeSessionDto>())!;
            replacement = current.Items.Single(i => i.Position == item.Position);
            Assert.IsTrue(seen.Add(replacement.SourceQuestionId));
        }
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await Http.PostAsJsonAsync(
            url + $"/items/{replacement.Id}/replace", request)).StatusCode);
        var exhausted = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        CollectionAssert.AreEqual(current.Items.Select(i => i.Id).ToArray(), exhausted.Items.Select(i => i.Id).ToArray());
        Assert.AreEqual(0, _ollama.Questions.Count, "Replacement feedback must not invoke grading.");
        _ollama.Response = "{\"Score\":0,\"IsCorrect\":false,\"Comment\":\"Incorrect\"}";
        (await Http.PostAsJsonAsync(url + $"/items/{replacement.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "wrong"})).EnsureSuccessStatusCode();
        Assert.AreEqual(HttpStatusCode.Conflict, (await Http.PostAsJsonAsync(
            url + $"/items/{replacement.Id}/replace", request)).StatusCode);
        var replay = (await (await Http.PostAsJsonAsync(replaceUrl, request)).Content.ReadFromJsonAsync<PracticeSessionDto>())!;
        Assert.AreEqual(1, replay.AnsweredCount);
    }

    [TestMethod]
    public async Task ReplacementRequiresOwnershipValidReasonAndEligibleCandidates()
    {
        var owner = await Login("replace-rights");
        var (mistakeId, questions) = await SeedPractice(owner, 6);
        var url = $"/api/PracticeSessions/{Guid.NewGuid()}";
        (await Http.PutAsJsonAsync(url, new StartPracticeSessionDto {MistakeId = mistakeId})).EnsureSuccessStatusCode();
        var initial = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        var replaceUrl = url + $"/items/{initial.Items[0].Id}/replace";
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(replaceUrl, new { })).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(replaceUrl, new {reason = 99})).StatusCode);
        var request = new ReplacePracticeSessionItemDto {Reason = PracticeReplacementReason.QuestionProblem};
        await Login("replace-foreign");
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.PostAsJsonAsync(replaceUrl, request)).StatusCode);
        Http.DefaultRequestHeaders.Authorization = null;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.PostAsJsonAsync(replaceUrl, request)).StatusCode);
        await Login("replace-rights");
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var category = await db.QuestionCategories.SingleAsync(c => c.Id == questions[0].QuestionCategoryId);
            category.IsFree = false; await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await Http.PostAsJsonAsync(replaceUrl, request)).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.AreEqual(3, await db.PracticeSessionItems.CountAsync());
            Assert.IsFalse(await db.PracticeSessionItems.AnyAsync(i => i.ReplacedAt != null));
            Assert.AreEqual(0, (await db.PracticeSessions.SingleAsync()).ReplacementVersion);
            var settings = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
            await settings.UpdateSettingAsync(SettingsMap.EnableEmbeddingBasedSearch, "false");
        }
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await Http.PostAsJsonAsync(replaceUrl, request)).StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentReplacementsOfDifferentPositionsKeepDistinctCandidates()
    {
        var owner = await Login("replace-two-positions");
        var (mistakeId, _) = await SeedPractice(owner, 6);
        var url = $"/api/PracticeSessions/{Guid.NewGuid()}";
        (await Http.PutAsJsonAsync(url, new StartPracticeSessionDto {MistakeId = mistakeId})).EnsureSuccessStatusCode();
        var initial = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        var responses = await Task.WhenAll(initial.Items.Take(2).Select(i => Http.PostAsJsonAsync(
            url + $"/items/{i.Id}/replace", new ReplacePracticeSessionItemDto {Reason = PracticeReplacementReason.NotRelated})));
        Assert.IsTrue(responses.Any(r => r.StatusCode == HttpStatusCode.OK));
        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        var current = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        Assert.AreEqual(3, current.Items.Select(i => i.Position).Distinct().Count());
        Assert.AreEqual(3, current.Items.Select(i => i.SourceQuestionId).Distinct().Count());
        Assert.IsTrue(current.Items.Count(i => i.QuestionType is QuestionType.ShortAnswer or QuestionType.Essay) <= 1);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(responses.Count(r => r.StatusCode == HttpStatusCode.OK),
            await db.PracticeSessionItems.CountAsync(i => i.ReplacedAt != null));
        Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
    }

    [TestMethod]
    public async Task ConcurrentAnswersAndReplacementsCommitOnlyOneOutcome()
    {
        var owner = await Login("replace-race");
        var (mistakeId, _) = await SeedPractice(owner, 6);
        var url = $"/api/PracticeSessions/{Guid.NewGuid()}";
        (await Http.PutAsJsonAsync(url, new StartPracticeSessionDto {MistakeId = mistakeId})).EnsureSuccessStatusCode();
        var initial = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        var first = initial.Items.First(i => i.QuestionType == QuestionType.Choice);
        var responses = await Task.WhenAll(
            Http.PostAsJsonAsync(url + $"/items/{first.Id}/answer", new PracticeSessionAnswerDto {UserAnswer = "wrong"}),
            Http.PostAsJsonAsync(url + $"/items/{first.Id}/replace", new ReplacePracticeSessionItemDto {Reason = PracticeReplacementReason.NotRelated}));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var old = await db.PracticeSessionItems.SingleAsync(i => i.Id == first.Id);
        Assert.AreNotEqual(old.SubmittedAt.HasValue, old.ReplacedAt.HasValue);
        Assert.AreEqual(old.SubmittedAt.HasValue ? 1 : 0, await db.UserPracticeHistories.CountAsync());
        var active = await db.PracticeSessionItems.Where(i => i.ReplacedAt == null).ToArrayAsync();
        Assert.AreEqual(3, active.Select(i => i.Position).Distinct().Count());
        Assert.AreEqual(3, active.Select(i => i.SourceQuestionId).Distinct().Count());
    }

    [TestMethod]
    public async Task PracticeSessionsRecheckRightsAndIsolateOwners()
    {
        var owner = await Login("rights-owner");
        var (mistakeId, questions) = await SeedPractice(owner);
        var id = Guid.NewGuid(); var url = $"/api/PracticeSessions/{id}";
        (await Http.PutAsJsonAsync(url, new StartPracticeSessionDto { MistakeId = mistakeId })).EnsureSuccessStatusCode();
        var initial = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        var item = initial.Items[0];
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "   "})).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = new string('x', 1025)})).StatusCode);
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var period = await db.QuestionCategories.SingleAsync(c => c.Id == questions[0].QuestionCategoryId);
            period.IsFree = false; await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, (await Http.PutAsJsonAsync($"/api/PracticeSessions/{Guid.NewGuid()}",
            new StartPracticeSessionDto { MistakeId = mistakeId })).StatusCode);
        var locked = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        Assert.IsTrue(locked.Items.All(i => i.Availability == "MembershipRequired" && i.Content == null && i.Metadata == null));
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "A"})).StatusCode);
        await Login("foreign-owner");
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync(url)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "A"})).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.PutAsJsonAsync($"/api/PracticeSessions/{Guid.NewGuid()}",
            new StartPracticeSessionDto {MistakeId = mistakeId})).StatusCode);
        await Login("rights-owner");
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.Questions.Remove(await db.Questions.SingleAsync(q => q.Id == item.SourceQuestionId));
            await db.SaveChangesAsync();
            Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
            Assert.AreEqual(1, await db.MistakeRecords.CountAsync());
        }
        var deleted = (await Http.GetFromJsonAsync<PracticeSessionDto>(url))!;
        Assert.AreEqual("QuestionUnavailable", deleted.Items.Single(i => i.Id == item.Id).Availability);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "A"})).StatusCode);
        Http.DefaultRequestHeaders.Authorization = null;
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync(url)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.PutAsJsonAsync(url,
            new StartPracticeSessionDto {MistakeId = mistakeId})).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.PostAsJsonAsync(url + $"/items/{item.Id}/answer",
            new PracticeSessionAnswerDto {UserAnswer = "A"})).StatusCode);
    }

    [TestMethod]
    public async Task RelatedCandidatesAreOwnerScopedAndNeverExposeAnswersOrPaidPractice()
    {
        var other = await Login("related-other");
        var owner = await Login("related-owner");
        var copy = Copy(owner, Guid.NewGuid(), order: 1);
        var foreign = Copy(other, copy.SourceQuestionId, order: 2);
        await Seed(copy, foreign);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<IGlobalSettingsService>();
        await settings.UpdateSettingAsync(SettingsMap.EnableEmbeddingBasedSearch, "true");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingOllamaInstance, "https://embedding.example.invalid");
        await settings.UpdateSettingAsync(SettingsMap.EmbeddingModel, "bge-m3:latest");
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var freePeriod = new QuestionCategory { Title = "Free period", IsFree = true,
            TotalCategoryId = period.TotalCategoryId, TotalCategory = period.TotalCategory };
        var source = new Question { Id = copy.SourceQuestionId!.Value, QuestionCategory = period,
            Content = copy.Content, QuestionType = copy.QuestionType, Metadata = copy.Metadata,
            StandardAnswer = copy.StandardAnswer, Explanation = copy.Explanation };
        var free = new Question { Id = Guid.NewGuid(), QuestionCategory = freePeriod,
            Content = "A free related exercise", StandardAnswer = "PRIVATE REFERENCE", Explanation = "PRIVATE EXPLANATION" };
        var paid = new Question { Id = Guid.NewGuid(), QuestionCategory = period,
            Content = "A paid related exercise", StandardAnswer = "PAID REFERENCE" };
        db.AddRange(source, free, paid); await db.SaveChangesAsync();
        foreach (var (q, similarity) in new[] { (source, 1f), (free, 0.8f), (paid, 0.9f) })
        {
            var vector = new float[1024]; vector[0] = similarity; vector[1] = MathF.Sqrt(1 - similarity * similarity);
            q.Embedding = EmbeddingHelper.Serialize(vector); q.LastEmbeddedAt = q.LastUpdatedAt;
        }
        await db.SaveChangesAsync();
        var response = await Http.GetAsync($"/api/Mistakes/{copy.Id}/related?userId={other}");
        response.EnsureSuccessStatusCode();
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<RelatedQuestionsDto>())!;
        Assert.IsTrue(result.Available); Assert.HasCount(1, result.Items);
        Assert.AreEqual(free.Id, result.Items.Single().Id);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PRIVATE", json); Assert.DoesNotContain("PAID", json);
        Assert.DoesNotContain("standardAnswer", json); Assert.DoesNotContain("explanation", json);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Http.GetAsync($"/api/Questions/{paid.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync($"/api/Questions/{free.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/Mistakes/{foreign.Id}/related")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/Mistakes/{Guid.NewGuid()}/related")).StatusCode);
        freePeriod.IsFree = false; await db.SaveChangesAsync();
        var afterPricingChange = (await Http.GetFromJsonAsync<RelatedQuestionsDto>($"/api/Mistakes/{copy.Id}/related"))!;
        Assert.HasCount(0, afterPricingChange.Items, "Cached vectors must not cache yesterday's access rights.");
        Assert.AreEqual(2, await db.MistakeRecords.CountAsync());
        Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
    }

    [TestMethod]
    public async Task ListsOnlyOwnQuestionsAndKeepsEveryPublishedVersionInAttempts()
    {
        var other = await Login("mistakes-other");
        var owner = await Login("mistakes-owner");
        var question = Guid.NewGuid();
        var old = Copy(owner, question, source: MistakeSource.QuestionBank, order: 1);
        var mock = Copy(owner, question, Guid.NewGuid(), MistakeSource.MockPaper, 2);
        var latest = Copy(owner, question, Guid.NewGuid(), MistakeSource.RealPaper, 3);
        await Seed(old, mock, latest, Copy(other, question, order: 4));
        var response = await Http.GetAsync("/api/Mistakes?userId=" + other);
        Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        var list = (await response.Content.ReadFromJsonAsync<MistakePageDto<MistakeSummaryDto>>())!;
        Assert.AreEqual(1, list.Total);
        Assert.AreEqual(latest.Id, list.Items.Single().Id);
        Assert.AreEqual(3, list.Items.Single().MistakeCount);
        Assert.AreEqual(latest.Content, list.Items.Single().Content);
        Assert.AreEqual(latest.QuestionType, list.Items.Single().QuestionType);
        Assert.AreEqual(latest.Source, list.Items.Single().Source);
        Assert.AreEqual(latest.SourcePaperTitle, list.Items.Single().SourcePaperTitle);
        Assert.AreEqual(latest.CreationTime, list.Items.Single().CreationTime);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("standardAnswer", json);
        Assert.DoesNotContain("userAnswer", json);
        Assert.DoesNotContain("gradingComment", json);
        Assert.DoesNotContain(other, json);
        var detailResponse = await Http.GetAsync("/api/Mistakes/" + old.Id);
        Assert.IsTrue(detailResponse.Headers.CacheControl!.NoStore);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<MistakeDetailsDto>())!;
        Assert.AreEqual(old.Content, detail.Content);
        Assert.AreEqual(old.Metadata, detail.Metadata);
        Assert.AreEqual(old.UserAnswer, detail.UserAnswer);
        Assert.AreEqual(old.StandardAnswer, detail.StandardAnswer);
        Assert.AreEqual(old.Explanation, detail.Explanation);
        Assert.AreEqual(old.GradingComment, detail.GradingComment);
        Assert.AreEqual(old.Score, detail.Score);
        Assert.AreEqual(old.MaxScore, detail.MaxScore);
        // There are deliberately no live questions, papers or purchases for these provenance IDs.
        var attempts = (await Http.GetFromJsonAsync<MistakePageDto<MistakeDetailsDto>>($"/api/Mistakes/{old.Id}/attempts?offset=1&count=1"))!;
        Assert.AreEqual(3, attempts.Total);
        Assert.AreEqual(mock.Id, attempts.Items.Single().Id);
        Assert.AreEqual(mock.SourcePaperTitle, attempts.Items.Single().SourcePaperTitle);
        Assert.AreEqual(mock.SourcePaperVersion, attempts.Items.Single().SourcePaperVersion);
        Assert.AreEqual(mock.StandardAnswer, attempts.Items.Single().StandardAnswer);
    }

    [TestMethod]
    public async Task MissingOriginalsAndIdsNeverMergeUnrelatedQuestionsAndPagingIsStable()
    {
        var user = await Login("mistakes-paging");
        var id = Guid.NewGuid();
        var snapshot = Copy(user, snapshotId: id, order: 1);
        var sameSnapshot = Copy(user, snapshotId: id, order: 2);
        var sameValueDifferentKind = Copy(user, id, order: 3);
        var firstUnknown = Copy(user, order: 4);
        var secondUnknown = Copy(user, order: 5);
        await Seed(snapshot, sameSnapshot, sameValueDifferentKind, firstUnknown, secondUnknown);
        var page = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?count=2"))!;
        Assert.AreEqual(4, page.Total);
        CollectionAssert.AreEqual(new[] { secondUnknown.Id, firstUnknown.Id }, page.Items.Select(m => m.Id).ToArray());
        var next = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?offset=2&count=2"))!;
        CollectionAssert.AreEqual(new[] { sameValueDifferentKind.Id, sameSnapshot.Id }, next.Items.Select(m => m.Id).ToArray());
        Assert.AreEqual(2, next.Items.Last().MistakeCount);
        var again = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?offset=2&count=2"))!;
        CollectionAssert.AreEqual(next.Items.Select(m => m.Id).ToArray(), again.Items.Select(m => m.Id).ToArray());
        var empty = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?offset=50"))!;
        Assert.AreEqual(4, empty.Total);
        Assert.AreEqual(0, empty.Items.Count);
        Assert.AreEqual(HttpStatusCode.NoContent, (await Http.DeleteAsync("/api/Mistakes/" + snapshot.Id)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Http.GetAsync("/api/Mistakes/" + sameValueDifferentKind.Id)).StatusCode);
        var unknownAttempts = (await Http.GetFromJsonAsync<MistakePageDto<MistakeDetailsDto>>($"/api/Mistakes/{firstUnknown.Id}/attempts"))!;
        Assert.AreEqual(1, unknownAttempts.Total);
    }

    [TestMethod]
    public async Task EqualTimesUseTheSameStableIdOrderForGroupsAndAttempts()
    {
        var owner = await Login("mistakes-tied-times");
        var question = Guid.NewGuid();
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var old = Copy(owner, question, order: 1, time: time);
        var other = Copy(owner, Guid.NewGuid(), order: 2, time: time);
        var latest = Copy(owner, question, order: 3, time: time);
        await Seed(old, other, latest);
        var first = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?count=1"))!;
        Assert.AreEqual(2, first.Total);
        Assert.AreEqual(latest.Id, first.Items.Single().Id);
        var second = (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes?offset=1&count=1"))!;
        Assert.AreEqual(other.Id, second.Items.Single().Id);
        var attempts = (await Http.GetFromJsonAsync<MistakePageDto<MistakeDetailsDto>>($"/api/Mistakes/{old.Id}/attempts"))!;
        CollectionAssert.AreEqual(new[] { latest.Id, old.Id }, attempts.Items.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public async Task ForeignIdsAreHiddenAndRemovingAQuestionOrClearingNeverDeletesHistory()
    {
        var other = await Login("mistakes-other-removal");
        var owner = await Login("mistakes-owner-removal");
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var question = new Question { Content = "Retained question", StandardAnswer = "Reference",
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true) };
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        var history = new UserPracticeHistory { Id = Guid.NewGuid(), UserId = owner, QuestionId = question.Id,
            UserAnswer = "Original wrong answer", IsCorrect = false };
        db.UserPracticeHistories.Add(history);
        await db.SaveChangesAsync();
        var foreign = Copy(other, question.Id, order: 1);
        var first = Copy(owner, question.Id, order: 2);
        var second = Copy(owner, question.Id, Guid.NewGuid(), MistakeSource.RealPaper, 3);
        var unrelated = Copy(owner, Guid.NewGuid(), order: 4);
        await Seed(foreign, first, second, unrelated);
        foreach (var id in new[] { foreign.Id, Guid.NewGuid() })
        {
            Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync("/api/Mistakes/" + id)).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/api/Mistakes/{id}/attempts")).StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, (await Http.DeleteAsync("/api/Mistakes/" + id)).StatusCode);
        }
        Assert.AreEqual(HttpStatusCode.NoContent, (await Http.DeleteAsync("/api/Mistakes/" + first.Id)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync("/api/Mistakes/" + second.Id)).StatusCode);
        Assert.IsTrue(await db.MistakeRecords.AnyAsync(m => m.Id == foreign.Id));
        Assert.IsTrue(await db.MistakeRecords.AnyAsync(m => m.Id == unrelated.Id));
        Assert.AreEqual(1, (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes"))!.Total);
        Assert.AreEqual(HttpStatusCode.NoContent, (await Http.DeleteAsync("/api/Mistakes?userId=" + other)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NoContent, (await Http.DeleteAsync("/api/Mistakes")).StatusCode);
        Assert.AreEqual(0, (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes"))!.Total);
        Assert.AreEqual(1, await db.MistakeRecords.CountAsync());
        Assert.AreEqual(history.Id, (await db.UserPracticeHistories.SingleAsync()).Id);
        Assert.AreEqual("Original wrong answer", (await db.UserPracticeHistories.SingleAsync()).UserAnswer);
        Assert.AreEqual(question.Content, (await db.Questions.SingleAsync()).Content);
        var response = await Http.PostAsJsonAsync("/api/UserPracticeHistory", new CreateUserPracticeHistoryDto
            { QuestionId = question.Id, UserAnswer = "Wrong again" });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual(1, (await Http.GetFromJsonAsync<MistakePageDto<MistakeSummaryDto>>("/api/Mistakes"))!.Total);
        Assert.AreEqual(2, await db.UserPracticeHistories.CountAsync());
    }

    [TestMethod]
    [DataRow("offset=-1")]
    [DataRow("count=0")]
    [DataRow("count=51")]
    [DataRow("count=invalid")]
    public async Task InvalidPagingIsRejectedForBothListAndAttempts(string query)
    {
        var user = await Login("mistakes-invalid");
        var entry = Copy(user);
        await Seed(entry);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync("/api/Mistakes?" + query)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Http.GetAsync($"/api/Mistakes/{entry.Id}/attempts?{query}")).StatusCode);
    }
}
