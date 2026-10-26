using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;



using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionsLoopTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService;
    private readonly StubWeChatPayService _mockWeChatPayService;
    private readonly StubDistributionChannelService _mockDistributionChannelService;

    public QuestionsLoopTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _http = new HttpClient(handler);
        _mockWeChatService = new StubWeChatService();
        _mockWeChatPayService = new StubWeChatPayService();
        _mockDistributionChannelService = new StubDistributionChannelService();
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockWeChatService = _mockWeChatService;
        TestStartupWithMockWeChat.MockWeChatPayService = _mockWeChatPayService;
        TestStartupWithMockWeChat.MockDistributionChannelService = _mockDistributionChannelService;
        
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<TestStartupWithMockWeChat>([], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(_server);
            await _server.UpdateDbAsync<WeChatExamDbContext>();
            await _server.SeedAsync();
            await _server.StartAsync();
        }
        catch
        {
            _server.Dispose();
            throw;
        }

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

        _mockWeChatService.DefaultSession = new WeChatSessionResult
            {
                IsSuccess = true,
                OpenId = openId,
                SessionKey = sessionKey
            };

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto
        {
            Code = code
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
    }

    [TestMethod]
    public async Task GetQuestions_ResumeType_LoopsBackToStart()
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
                    QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
                    Content = $"Loop Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    CreationTime = DateTime.UtcNow.AddMinutes(i) // i=0 is oldest
                };
                context.Questions.Add(q);
                questionIds.Add(q.Id);
            }
            await context.SaveChangesAsync();
        }

        // 1. Submit practice history for ALL questions
        foreach (var qId in questionIds)
        {
            var historyDto = new CreateUserPracticeHistoryDto
            {
                QuestionId = qId,
                UserAnswer = "A",
                PracticeType = PracticeType.QuestionType
            };
            var postResponse = await _http.PostAsJsonAsync("/api/UserPracticeHistory", historyDto);
            postResponse.EnsureSuccessStatusCode();
            
            // Wait a bit to ensure CreationTime of history is different if needed, 
            // though the logic uses Question.CreationTime.
            await Task.Delay(10); 
        }

        // 2. Resume (ResumeType=0, Size=3)
        // Currently, this should return 0 questions because all questions have been practiced.
        var resumeResponse = await _http.GetAsync("/api/Questions?size=3&resumeType=0");
        resumeResponse.EnsureSuccessStatusCode();
        var resumeQuestions = await resumeResponse.Content.ReadFromJsonAsync<List<QuestionDto>>();

        Assert.IsNotNull(resumeQuestions);
        
        // Before fix, this fails because it's empty.
        // After fix, it should return questions starting from the beginning.
        Assert.AreEqual(3, resumeQuestions.Count, "Should loop back to start when all questions are finished.");
        Assert.AreEqual("Loop Question 0", resumeQuestions[0].Content);
    }
}
