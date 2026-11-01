using System.Net;
using System.Net.Http.Headers;
using Aiursoft.DbTools;
using Aiursoft.WebTools.Abstractions.Models;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

// Exercise real relational grouping and bulk deletion, as well as the normal Bearer authentication middleware.
public class MistakesApiTestStartup : TestStartupWithMockWeChat, IWebStartup
{
    public static string ConnectionString { get; set; } = string.Empty;

    public new void ConfigureServices(IConfiguration configuration, IWebHostEnvironment environment, IServiceCollection services)
    {
        base.ConfigureServices(configuration, environment, services);
        services.AddDbContext<SqliteContext>(options => options.UseSqlite(ConnectionString));
        services.AddScoped<WeChatExamDbContext>(provider => provider.GetRequiredService<SqliteContext>());
    }
}

[TestClass]
public class MistakesApiTests : TestBase
{
    private readonly StubWeChatService _wechat = new();
    private SqliteConnection? _keeper;
    private string? _storage;

    [TestInitialize]
    public override async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockWeChatService = _wechat;
        TestStartupWithMockWeChat.MockWeChatPayService = new StubWeChatPayService();
        TestStartupWithMockWeChat.MockDistributionChannelService = new StubDistributionChannelService();
        TestStartupWithMockWeChat.MockOllamaService = new StubOllamaService();
        TestStartupWithMockWeChat.MockVipProductService = null;
        _keeper = new SqliteConnection($"Data Source=mistakes-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True");
        await _keeper.OpenAsync();
        MistakesApiTestStartup.ConnectionString = _keeper.ConnectionString;
        _storage = Directory.CreateTempSubdirectory("MistakesApi-").FullName;
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<MistakesApiTestStartup>([$"Storage:Path={_storage}"], port: reservation.Port);
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
            foreach (var url in new[] { "/api/Mistakes", $"/api/Mistakes/{Guid.NewGuid()}", $"/api/Mistakes/{Guid.NewGuid()}/attempts" })
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.GetAsync(url)).StatusCode);
            foreach (var url in new[] { "/api/Mistakes", $"/api/Mistakes/{Guid.NewGuid()}" })
                Assert.AreEqual(HttpStatusCode.Unauthorized, (await Http.DeleteAsync(url)).StatusCode);
        }
        await AssertUnauthorized();
        await RegisterAndLoginAsync();
        await AssertUnauthorized();
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
