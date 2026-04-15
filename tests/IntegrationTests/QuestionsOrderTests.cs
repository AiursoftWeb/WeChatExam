using System.Net;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WebTools;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class QuestionsOrderTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public QuestionsOrderTests()
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
        
        _server = await Extends.AppAsync<TestStartupWithMockWeChat>([], port: _port);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
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
        if (_server != null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }
    }

    [TestMethod]
    public async Task GetQuestions_OrderIndex_SortingPriority()
    {
        // 1. Arrange: Seed questions with mixed OrderIndex and CreationTime
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var category = new Category { Id = Guid.NewGuid(), Title = "Test Category", ParentId = null };
            db.Categories.Add(category);

            // Q1: No order, creation time T1
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                Content = "Q1_NoOrder_T1",
                CategoryId = category.Id,
                OrderIndex = null,
                CreationTime = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc)
            });

            // Q2: Order 2, creation time T2 (late)
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                Content = "Q2_Order2_T2",
                CategoryId = category.Id,
                OrderIndex = 2,
                CreationTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            });

            // Q3: Order 1, creation time T3 (latest)
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                Content = "Q3_Order1_T3",
                CategoryId = category.Id,
                OrderIndex = 1,
                CreationTime = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc)
            });

            // Q4: No order, creation time T0 (earliest)
            db.Questions.Add(new Question
            {
                Id = Guid.NewGuid(),
                Content = "Q4_NoOrder_T0",
                CategoryId = category.Id,
                OrderIndex = null,
                CreationTime = new DateTime(2026, 1, 1, 09, 0, 0, DateTimeKind.Utc)
            });

            await db.SaveChangesAsync();
        }

        // 2. Auth
        _mockWeChatService.Setup(s => s.CodeToSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new WeChatSessionResult { IsSuccess = true, OpenId = "test-user", SessionKey = "test-key" });
        
        var authResponse = await _http.PostAsJsonAsync("/api/auth/login", new Code2SessionDto { Code = "test-code" });
        Assert.AreEqual(HttpStatusCode.OK, authResponse.StatusCode);
        var tokenDto = await authResponse.Content.ReadFromJsonAsync<TokenDto>();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenDto!.Token);

        // 3. Act: Fetch questions sequentially (Resume Mode or Pagination)
        var response = await _http.GetAsync("/api/questions?size=10&page=1");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var questions = await response.Content.ReadFromJsonAsync<List<QuestionDto>>();

        // 4. Assert: Correct order should be: Q3 (Order 1), Q2 (Order 2), Q4 (No Order, T0), Q1 (No Order, T1)
        Assert.IsNotNull(questions);
        Assert.AreEqual(4, questions.Count);
        
        Assert.AreEqual("Q3_Order1_T3", questions[0].Content);
        Assert.AreEqual("Q2_Order2_T2", questions[1].Content);
        Assert.AreEqual("Q4_NoOrder_T0", questions[2].Content);
        Assert.AreEqual("Q1_NoOrder_T1", questions[3].Content);
    }

    [TestMethod]
    public async Task GetQuestions_ResumeMode_OrderIndex_Sequence()
    {
        // 1. Arrange
        Guid q1Id, q2Id, q3Id, q4Id;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var category = new Category { Id = Guid.NewGuid(), Title = "Test Category", ParentId = null };
            db.Categories.Add(category);

            q1Id = Guid.NewGuid();
            db.Questions.Add(new Question { Id = q1Id, Content = "Q1_Order1", CategoryId = category.Id, OrderIndex = 1, CreationTime = DateTime.UtcNow.AddMinutes(-4) });
            q2Id = Guid.NewGuid();
            db.Questions.Add(new Question { Id = q2Id, Content = "Q2_Order2", CategoryId = category.Id, OrderIndex = 2, CreationTime = DateTime.UtcNow.AddMinutes(-3) });
            q3Id = Guid.NewGuid();
            db.Questions.Add(new Question { Id = q3Id, Content = "Q3_NoOrder", CategoryId = category.Id, OrderIndex = null, CreationTime = DateTime.UtcNow.AddMinutes(-2) });
            q4Id = Guid.NewGuid();
            db.Questions.Add(new Question { Id = q4Id, Content = "Q4_NoOrder", CategoryId = category.Id, OrderIndex = null, CreationTime = DateTime.UtcNow.AddMinutes(-1) });

            await db.SaveChangesAsync();
        }

        // 2. Auth
        _mockWeChatService.Setup(s => s.CodeToSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new WeChatSessionResult { IsSuccess = true, OpenId = "test-user-resume", SessionKey = "test-key" });
        
        var authResponse = await _http.PostAsJsonAsync("/api/auth/login", new Code2SessionDto { Code = "test-code" });
        Assert.AreEqual(HttpStatusCode.OK, authResponse.StatusCode);
        var tokenDto = await authResponse.Content.ReadFromJsonAsync<TokenDto>();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenDto!.Token);

        string actualUserId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.MiniProgramOpenId == "test-user-resume");
            actualUserId = user!.Id;
        }

        // 3. Act & Assert: Sequential progress
        
        // Fetch first 2
        var res1 = await _http.GetAsync("/api/questions?size=2&resumeType=0");
        var list1 = await res1.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.AreEqual(2, list1!.Count);
        Assert.AreEqual("Q1_Order1", list1[0].Content);
        Assert.AreEqual("Q2_Order2", list1[1].Content);

        // Record practice for Q2
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.UserPracticeHistories.Add(new UserPracticeHistory 
            { 
                UserId = actualUserId, 
                QuestionId = q2Id, 
                PracticeType = PracticeType.QuestionType, 
                CreationTime = DateTime.UtcNow,
                UserAnswer = "Any",
                IsCorrect = true
            });
            await db.SaveChangesAsync();
        }

        // Fetch next (should be Q3)
        var res2 = await _http.GetAsync("/api/questions?size=2&resumeType=0");
        var list2 = await res2.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.AreEqual(2, list2!.Count);
        Assert.AreEqual("Q3_NoOrder", list2[0].Content);
        Assert.AreEqual("Q4_NoOrder", list2[1].Content);

        // Record practice for Q4
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.UserPracticeHistories.Add(new UserPracticeHistory 
            { 
                UserId = actualUserId, 
                QuestionId = q4Id, 
                PracticeType = PracticeType.QuestionType, 
                CreationTime = DateTime.UtcNow,
                UserAnswer = "Any",
                IsCorrect = true
            });
            await db.SaveChangesAsync();
        }

        // Loop back check
        var res3 = await _http.GetAsync("/api/questions?size=2&resumeType=0");
        var list3 = await res3.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.AreEqual(2, list3!.Count);
        Assert.AreEqual("Q1_Order1", list3[0].Content);
    }
}
