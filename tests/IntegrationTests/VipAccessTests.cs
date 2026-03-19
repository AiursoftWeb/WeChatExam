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
public class VipAccessTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public VipAccessTests()
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
        await _server.StartAsync();

        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            context.Papers.RemoveRange(context.Papers);
            context.Categories.RemoveRange(context.Categories);
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

    private async Task<string> LoginAsync()
    {
        var openId = "test-user";
        _mockWeChatService.Setup(x => x.CodeToSessionAsync(It.IsAny<string>()))
            .ReturnsAsync(new WeChatSessionResult { IsSuccess = true, OpenId = openId });

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = "code" });
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
    }

    [TestMethod]
    public async Task GetPapers_AccessLogic_RespectsVipTypes()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Guid catId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paperService = scope.ServiceProvider.GetRequiredService<IPaperService>();

            var cat = new Category { Title = "Cat", ParentId = null };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();
            catId = cat.Id;

            // 1. Free paper
            var pFree = await paperService.CreatePaperAsync("Free", 60, true);
            await paperService.SetStatusAsync(pFree.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(pFree.Id);

            // 2. Real Exam paper
            var pReal = await paperService.CreatePaperAsync("Real", 60, false, true);
            await paperService.SetStatusAsync(pReal.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(pReal.Id);

            // 3. Category paper
            var pCat = await paperService.CreatePaperAsync("Cat Paper", 60, false);
            await paperService.AssociateCategoryAsync(pCat.Id, catId);
            await paperService.SetStatusAsync(pCat.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(pCat.Id);
        }

        // Scenario A: No VIP
        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership>());
        
        var resA = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsTrue(resA!.First(p => p.Title == "Free").HasAccess);
        Assert.IsFalse(resA!.First(p => p.Title == "Real").HasAccess);
        Assert.IsFalse(resA!.First(p => p.Title == "Cat Paper").HasAccess);

        // Scenario B: Has Real Exam VIP
        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership> { 
                new VipMembership { 
                    UserId = "u", 
                    StartTime = DateTime.UtcNow.AddDays(-1), 
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "R", Type = VipProductType.RealExam }
                } 
            });
            
        var resB = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsTrue(resB!.First(p => p.Title == "Real").HasAccess);
        Assert.IsFalse(resB!.First(p => p.Title == "Cat Paper").HasAccess);

        // Scenario C: Has Category VIP
        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership> { 
                new VipMembership { 
                    UserId = "u", 
                    StartTime = DateTime.UtcNow.AddDays(-1), 
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "C", Type = VipProductType.Category, CategoryId = catId }
                } 
            });
            
        var resC = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsFalse(resC!.First(p => p.Title == "Real").HasAccess);
        Assert.IsTrue(resC!.First(p => p.Title == "Cat Paper").HasAccess);
    }
}
