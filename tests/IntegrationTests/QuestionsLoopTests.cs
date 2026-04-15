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
public class QuestionsLoopTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public QuestionsLoopTests()
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
