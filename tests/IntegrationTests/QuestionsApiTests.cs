using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionsApiTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService;
    private readonly StubWeChatPayService _mockWeChatPayService;
    private readonly StubDistributionChannelService _mockDistributionChannelService;

    public QuestionsApiTests()
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
    public async Task GetQuestions_ParameterValidation_ReturnsBadRequest()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // 1. RandomSize & Size
        var response1 = await _http.GetAsync("/api/Questions?randomSize=10&size=10");
        Assert.AreEqual(HttpStatusCode.BadRequest, response1.StatusCode);

        // 2. Page & ResumeType
        var response2 = await _http.GetAsync("/api/Questions?page=1&resumeType=0&size=10");
        Assert.AreEqual(HttpStatusCode.BadRequest, response2.StatusCode);

        // 3. RandomSize & Page
        var response3 = await _http.GetAsync("/api/Questions?randomSize=10&page=1");
        Assert.AreEqual(HttpStatusCode.BadRequest, response3.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestions_RandomSize_ReturnsRandomQuestions()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Seed questions directly to DB to avoid API overhead and ensure we have enough data
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (int i = 0; i < 20; i++)
            {
                context.Questions.Add(new Question
                {
                    QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
                    Content = $"Random Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    CreationTime = DateTime.UtcNow.AddMinutes(-i)
                });
            }
            await context.SaveChangesAsync();
        }

        var response = await _http.GetAsync("/api/Questions?randomSize=5");
        response.EnsureSuccessStatusCode();
        var questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();

        Assert.IsNotNull(questions);
        Assert.AreEqual(5, questions.Count);
        // Randomness is hard to test deterministically, but we can check if we got unique questions
        Assert.AreEqual(5, questions.Select(q => q.Id).Distinct().Count());
    }

    [TestMethod]
    public async Task GetQuestions_Pagination_ReturnsPaginatedQuestions()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            // Add questions with distinct creation times
            for (int i = 0; i < 20; i++)
            {
                context.Questions.Add(new Question
                {
                    QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
                    Content = $"Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    // Older questions created first (i=0 is oldest)
                    CreationTime = DateTime.UtcNow.AddMinutes(i) 
                });
            }
            await context.SaveChangesAsync();
        }
        
        // Page 1, Size 5. Should return oldest 5 (since ordered by CreationTime ASC in pagination mode)
        var response1 = await _http.GetAsync("/api/Questions?size=5&page=1");
        response1.EnsureSuccessStatusCode();
        var page1 = await response1.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.AreEqual(5, page1!.Count);
        Assert.AreEqual("Question 0", page1[0].Content);
        Assert.AreEqual("Question 4", page1[4].Content);

        // Page 2, Size 5
        var response2 = await _http.GetAsync("/api/Questions?size=5&page=2");
        response2.EnsureSuccessStatusCode();
        var page2 = await response2.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.AreEqual(5, page2!.Count);
        Assert.AreEqual("Question 5", page2[0].Content);
        Assert.AreEqual("Question 9", page2[4].Content);
    }

    [TestMethod]
    public async Task GetQuestions_ResumeType_ReturnsNextQuestions()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        List<Guid> questionIds = new List<Guid>();
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (int i = 0; i < 10; i++)
            {
                var q = new Question
                {
                    QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(),
                    Content = $"Resume Question {i}",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch,
                    CreationTime = DateTime.UtcNow.AddMinutes(i) // i=0 is oldest
                };
                context.Questions.Add(q);
                questionIds.Add(q.Id);
            }
            await context.SaveChangesAsync();
        }

        // 1. Submit practice history for Question 2 (index 2)
        // Questions are 0..9. Sorted by time: 0, 1, 2, 3, ...
        // If I practice Question 2, resume should start from Question 3.
        
        // Get question 2 ID (which is questionIds[2])
        var lastPracticedId = questionIds[2];
        
        var historyDto = new CreateUserPracticeHistoryDto
        {
            QuestionId = lastPracticedId,
            UserAnswer = "A",
            PracticeType = PracticeType.QuestionType
        };
        
        var postResponse = await _http.PostAsJsonAsync("/api/UserPracticeHistory", historyDto);
        postResponse.EnsureSuccessStatusCode();

        // 2. Resume (ResumeType=0, Size=3)
        var resumeResponse = await _http.GetAsync("/api/Questions?size=3&resumeType=0");
        resumeResponse.EnsureSuccessStatusCode();
        var resumeQuestions = await resumeResponse.Content.ReadFromJsonAsync<List<QuestionDto>>();

        Assert.IsNotNull(resumeQuestions);
        Assert.AreEqual(3, resumeQuestions.Count);
        
        // Expected: Question 3, 4, 5
        Assert.AreEqual("Resume Question 3", resumeQuestions[0].Content);
        Assert.AreEqual("Resume Question 5", resumeQuestions[2].Content);
    }
}