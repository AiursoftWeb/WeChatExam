using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;




using Moq;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionLastFlagTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public QuestionLastFlagTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
        _mockWeChatService = new Mock<IWeChatService>();
        _mockWeChatPayService = new Mock<IWeChatPayService>();
        _mockDistributionChannelService = new Mock<IDistributionChannelService>();
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;
        TestStartupWithMockWeChat.MockWeChatPayService = _mockWeChatPayService;
        TestStartupWithMockWeChat.MockDistributionChannelService = _mockDistributionChannelService;
        
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();

        // Clear existing questions to ensure test isolation
        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            context.Questions.RemoveRange(context.Questions);
            context.UserPracticeHistories.RemoveRange(context.UserPracticeHistories);
            await context.SaveChangesAsync();
        }
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private async Task<string> LoginAsWeChatUserAsync()
    {
        var openId = $"test-openid-{Guid.NewGuid()}";
        var sessionKey = "test-session-key";
        var code = "test-code";

        _mockWeChatService.Setup(x => x.CodeToSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey
            });

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto
        {
            Code = code
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
    }

    [TestMethod]
    public async Task GetQuestions_ReturnsIsLastQuestion()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (int i = 0; i < 3; i++)
            {
                var q = new Question
                {
                    Content = $"Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    CreationTime = DateTime.UtcNow.AddMinutes(i)
                };
                context.Questions.Add(q);
            }
            await context.SaveChangesAsync();
        }

        // Test with sequential fetch
        var response = await _http.GetAsync("/api/Questions?size=10&type=0");
        response.EnsureSuccessStatusCode();
        var questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();

        Assert.IsNotNull(questions);
        Assert.AreEqual(3, questions.Count);
        Assert.IsFalse(questions[0].IsLastQuestion, "Question 0 should not be last");
        Assert.IsFalse(questions[1].IsLastQuestion, "Question 1 should not be last");
        Assert.IsTrue(questions[2].IsLastQuestion, "Question 2 should be last");
        
        // Test with paging (fetching first 2)
        response = await _http.GetAsync("/api/Questions?size=2&page=1&type=0");
        response.EnsureSuccessStatusCode();
        questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.IsNotNull(questions);
        Assert.AreEqual(2, questions.Count);
        Assert.IsFalse(questions[0].IsLastQuestion);
        Assert.IsFalse(questions[1].IsLastQuestion);
        
        // Test with paging (fetching last page)
        response = await _http.GetAsync("/api/Questions?size=2&page=2&type=0");
        response.EnsureSuccessStatusCode();
        questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.IsNotNull(questions);
        Assert.AreEqual(1, questions.Count);
        Assert.IsTrue(questions[0].IsLastQuestion, "The last question on the last page should have IsLastQuestion=true");
    }

    [TestMethod]
    public async Task GetQuestions_ResumeType_SetsIsLastQuestion()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        List<Guid> questionIds = new List<Guid>();
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (int i = 0; i < 3; i++)
            {
                var q = new Question
                {
                    Content = $"Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    CreationTime = DateTime.UtcNow.AddMinutes(i)
                };
                context.Questions.Add(q);
                questionIds.Add(q.Id);
            }
            await context.SaveChangesAsync();
        }

        // 1. Practice first 2 questions
        for (int i = 0; i < 2; i++)
        {
            var historyDto = new CreateUserPracticeHistoryDto
            {
                QuestionId = questionIds[i],
                UserAnswer = "A",
                PracticeType = PracticeType.QuestionType
            };
            await _http.PostAsJsonAsync("/api/UserPracticeHistory", historyDto);
        }

        // 2. Fetch next question (should be the 3rd one, which is the last)
        var response = await _http.GetAsync("/api/Questions?size=1&resumeType=0&type=0");
        response.EnsureSuccessStatusCode();
        var questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();
        
        Assert.IsNotNull(questions);
        Assert.AreEqual(1, questions.Count);
        Assert.AreEqual("Question 2", questions[0].Content);
        Assert.IsTrue(questions[0].IsLastQuestion, "The 3rd question should be marked as last");

        // 3. Practice the 3rd question
        var finalHistoryDto = new CreateUserPracticeHistoryDto
        {
            QuestionId = questionIds[2],
            UserAnswer = "A",
            PracticeType = PracticeType.QuestionType
        };
        await _http.PostAsJsonAsync("/api/UserPracticeHistory", finalHistoryDto);

        // 4. Fetch next (should loop to start)
        response = await _http.GetAsync("/api/Questions?size=1&resumeType=0&type=0");
        response.EnsureSuccessStatusCode();
        questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();
        
        Assert.IsNotNull(questions);
        Assert.AreEqual(1, questions.Count);
        Assert.AreEqual("Question 0", questions[0].Content);
        Assert.IsFalse(questions[0].IsLastQuestion, "Looped back question 0 should not be marked as last");
    }
}
