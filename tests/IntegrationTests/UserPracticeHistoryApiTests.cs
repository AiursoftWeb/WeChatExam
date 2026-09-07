using System.Net;
using System.Net.Http.Headers;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class UserPracticeHistoryApiTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private readonly Mock<IWeChatService> _mockWeChatService = new();
    private readonly Mock<IWeChatPayService> _mockWeChatPayService = new();
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService = new();
    private IHost? _server;

    public UserPracticeHistoryApiTests()
    {
        _port = Network.GetAvailablePort();
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;
        TestStartupWithMockWeChat.MockWeChatPayService = _mockWeChatPayService;
        TestStartupWithMockWeChat.MockDistributionChannelService = _mockDistributionChannelService;

        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.StartAsync();

        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        context.UserPracticeHistories.RemoveRange(context.UserPracticeHistories);
        context.Questions.RemoveRange(context.Questions);
        await context.SaveChangesAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server != null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }

        _http.Dispose();
    }

    [TestMethod]
    public async Task GetUserPracticeHistory_DefaultQuery_ReturnsOnlyCurrentUserInStableDescendingOrder()
    {
        var currentUser = await LoginAsync("history-current-user");
        var otherUser = await LoginAsync("history-other-user");
        var question = CreateQuestion(QuestionType.Choice);
        var newestId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var tiedHigherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var tiedLowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        await SeedAsync(
            [question],
            [
                CreateHistory(tiedLowerId, currentUser.UserId, question.Id, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), true),
                CreateHistory(tiedHigherId, currentUser.UserId, question.Id, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), false),
                CreateHistory(newestId, currentUser.UserId, question.Id, new DateTime(2026, 1, 2, 10, 0, 0, DateTimeKind.Utc), true),
                CreateHistory(Guid.NewGuid(), otherUser.UserId, question.Id, new DateTime(2026, 1, 3, 10, 0, 0, DateTimeKind.Utc), true)
            ]);

        UseToken(currentUser.Token);
        var histories = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>("/api/UserPracticeHistory");

        Assert.IsNotNull(histories);
        CollectionAssert.AreEqual(
            new[] { newestId, tiedHigherId, tiedLowerId },
            histories.Select(x => x.Id).ToArray());
        Assert.IsTrue(histories.All(x => x.QuestionId == question.Id));
        Assert.AreEqual(question.StandardAnswer, histories[0].StandardAnswer);
        Assert.AreEqual(question.Explanation, histories[0].Explanation);
    }

    [TestMethod]
    public async Task GetUserPracticeHistory_CombinedFilters_ReturnOnlyMatchingHistories()
    {
        var user = await LoginAsync("history-filter-user");
        var choiceQuestion = CreateQuestion(QuestionType.Choice);
        var blankQuestion = CreateQuestion(QuestionType.Blank);
        var start = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var startId = Guid.NewGuid();
        var endId = Guid.NewGuid();

        await SeedAsync(
            [choiceQuestion, blankQuestion],
            [
                CreateHistory(startId, user.UserId, choiceQuestion.Id, start, false),
                CreateHistory(endId, user.UserId, choiceQuestion.Id, end, false),
                CreateHistory(Guid.NewGuid(), user.UserId, choiceQuestion.Id, start.AddHours(1), true),
                CreateHistory(Guid.NewGuid(), user.UserId, choiceQuestion.Id, start.AddHours(2), false, null),
                CreateHistory(Guid.NewGuid(), user.UserId, blankQuestion.Id, start.AddHours(3), false),
                CreateHistory(Guid.NewGuid(), user.UserId, choiceQuestion.Id, start.AddTicks(-1), false),
                CreateHistory(Guid.NewGuid(), user.UserId, choiceQuestion.Id, end.AddTicks(1), false)
            ]);

        UseToken(user.Token);
        var url = $"/api/UserPracticeHistory?questionId={choiceQuestion.Id}" +
                  "&practiceType=0&questionType=0&isCorrect=false" +
                  "&startTime=2026-02-01T00%3A00%3A00Z&endTime=2026-02-02T00%3A00%3A00Z";
        var histories = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>(url);

        Assert.IsNotNull(histories);
        CollectionAssert.AreEquivalent(new[] { startId, endId }, histories.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task GetUserPracticeHistory_IsCorrectSupportsAllThreeStates()
    {
        var user = await LoginAsync("history-correct-user");
        var question = CreateQuestion(QuestionType.Bool);
        var correctId = Guid.NewGuid();
        var incorrectId = Guid.NewGuid();
        await SeedAsync(
            [question],
            [
                CreateHistory(correctId, user.UserId, question.Id, DateTime.UtcNow.AddMinutes(-1), true),
                CreateHistory(incorrectId, user.UserId, question.Id, DateTime.UtcNow, false)
            ]);
        UseToken(user.Token);

        var all = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>("/api/UserPracticeHistory");
        var correct = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>("/api/UserPracticeHistory?isCorrect=true");
        var incorrect = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>("/api/UserPracticeHistory?isCorrect=false");

        Assert.AreEqual(2, all!.Count);
        CollectionAssert.AreEqual(new[] { correctId }, correct!.Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(new[] { incorrectId }, incorrect!.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    public async Task GetUserPracticeHistory_OffsetAndCount_AreAppliedAfterFilteringAndStableOrdering()
    {
        var user = await LoginAsync("history-page-user");
        var choiceQuestion = CreateQuestion(QuestionType.Choice);
        var blankQuestion = CreateQuestion(QuestionType.Blank);
        var ids = Enumerable.Range(1, 4)
            .Select(i => Guid.Parse($"00000000-0000-0000-0000-{i:D12}"))
            .ToArray();
        var tiedTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        await SeedAsync(
            [choiceQuestion, blankQuestion],
            [
                CreateHistory(ids[0], user.UserId, choiceQuestion.Id, tiedTime.AddHours(-2), true),
                CreateHistory(ids[1], user.UserId, choiceQuestion.Id, tiedTime, true),
                CreateHistory(ids[2], user.UserId, choiceQuestion.Id, tiedTime, true),
                CreateHistory(ids[3], user.UserId, choiceQuestion.Id, tiedTime.AddHours(1), true),
                CreateHistory(Guid.NewGuid(), user.UserId, blankQuestion.Id, tiedTime.AddHours(2), true)
            ]);

        UseToken(user.Token);
        var url = "/api/UserPracticeHistory?questionType=0&offset=1&count=2";
        var first = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>(url);
        var second = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>(url);

        Assert.IsNotNull(first);
        CollectionAssert.AreEqual(new[] { ids[2], ids[1] }, first.Select(x => x.Id).ToArray());
        CollectionAssert.AreEqual(first.Select(x => x.Id).ToArray(), second!.Select(x => x.Id).ToArray());
    }

    [TestMethod]
    [DataRow("offset=-1")]
    [DataRow("count=0")]
    [DataRow("count=-1")]
    [DataRow("practiceType=999")]
    [DataRow("questionType=999")]
    [DataRow("startTime=2026-01-01T00%3A00%3A00%2B08%3A00")]
    [DataRow("endTime=2026-01-02T00%3A00%3A00%2B08%3A00")]
    [DataRow("startTime=2026-01-03T00%3A00%3A00Z&endTime=2026-01-02T00%3A00%3A00Z")]
    public async Task GetUserPracticeHistory_InvalidFilters_ReturnBadRequest(string query)
    {
        var user = await LoginAsync($"history-invalid-{Guid.NewGuid()}");
        UseToken(user.Token);

        var response = await _http.GetAsync($"/api/UserPracticeHistory?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task GetUserPracticeHistory_EqualTimeBounds_IncludeMatchingInstant()
    {
        var user = await LoginAsync("history-equal-time-user");
        var question = CreateQuestion(QuestionType.Choice);
        var historyId = Guid.NewGuid();
        await SeedAsync(
            [question],
            [CreateHistory(historyId, user.UserId, question.Id, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), true)]);
        UseToken(user.Token);

        var histories = await _http.GetFromJsonAsync<List<UserPracticeHistoryDto>>(
            "/api/UserPracticeHistory?startTime=2026-04-01T00%3A00%3A00Z&endTime=2026-04-01T00%3A00%3A00Z");

        Assert.IsNotNull(histories);
        CollectionAssert.AreEqual(new[] { historyId }, histories.Select(x => x.Id).ToArray());
    }

    private async Task<(string Token, string UserId)> LoginAsync(string openId)
    {
        _mockWeChatService.Setup(x => x.CodeToSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = "test-session-key"
            });

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = "test-code" });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<TokenDto>())!.Token;

        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var userId = await context.Users
            .Where(x => x.MiniProgramOpenId == openId)
            .Select(x => x.Id)
            .SingleAsync();
        return (token, userId);
    }

    private void UseToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task SeedAsync(IEnumerable<Question> questions, IEnumerable<UserPracticeHistory> histories)
    {
        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        context.Questions.AddRange(questions);
        context.UserPracticeHistories.AddRange(histories);
        await context.SaveChangesAsync();
    }

    private static Question CreateQuestion(QuestionType questionType)
    {
        return new Question
        {
            Id = Guid.NewGuid(),
            Content = $"Question {Guid.NewGuid()}",
            QuestionType = questionType,
            StandardAnswer = "Answer",
            Explanation = "Explanation"
        };
    }

    private static UserPracticeHistory CreateHistory(
        Guid id,
        string userId,
        Guid questionId,
        DateTime creationTime,
        bool isCorrect,
        PracticeType? practiceType = PracticeType.QuestionType)
    {
        return new UserPracticeHistory
        {
            Id = id,
            UserId = userId,
            QuestionId = questionId,
            UserAnswer = "User answer",
            IsCorrect = isCorrect,
            PracticeType = practiceType,
            CreationTime = creationTime
        };
    }
}
