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
public class PapersApiTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public PapersApiTests()
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
        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>())).ReturnsAsync(new List<VipMembership>());
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

        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            context.Papers.RemoveRange(context.Papers);
            context.Categories.RemoveRange(context.Categories);
            context.Tags.RemoveRange(context.Tags);
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
    public async Task GetPapers_Filtering_ReturnsFilteredPapers()
    {
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Guid cat1Id, cat2Id;
        int tag1Id;

        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paperService = scope.ServiceProvider.GetRequiredService<IPaperService>();
            var tagService = scope.ServiceProvider.GetRequiredService<ITagService>();

            var cat1 = new Category { Title = "Cat 1", ParentId = null };
            var cat2 = new Category { Title = "Cat 2", ParentId = null };
            context.Categories.AddRange(cat1, cat2);
            await context.SaveChangesAsync();
            cat1Id = cat1.Id;
            cat2Id = cat2.Id;

            var tag1 = await tagService.GetOrCreateTagAsync("Tag1");
            tag1Id = tag1.Id;

            // Paper 1: Cat1, Real, Tag1
            var p1 = await paperService.CreatePaperAsync("Paper 1", 60, true, true);
            await paperService.AssociateCategoryAsync(p1.Id, cat1Id);
            await tagService.AddTagToPaperAsync(p1.Id, tag1Id);
            await paperService.SetStatusAsync(p1.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(p1.Id);

            // Paper 2: Cat2, Not Real, No Tag
            var p2 = await paperService.CreatePaperAsync("Paper 2", 60, true);
            await paperService.AssociateCategoryAsync(p2.Id, cat2Id);
            await paperService.SetStatusAsync(p2.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(p2.Id);

            // Paper 3: Cat1, Not Real, Tag1
            var p3 = await paperService.CreatePaperAsync("Paper 3", 60, true);
            await paperService.AssociateCategoryAsync(p3.Id, cat1Id);
            await tagService.AddTagToPaperAsync(p3.Id, tag1Id);
            await paperService.SetStatusAsync(p3.Id, PaperStatus.Publishable);
            await paperService.PublishAsync(p3.Id);
        }

        // 1. Filter by Category
        var res1 = await _http.GetFromJsonAsync<List<PaperListDto>>($"/api/Papers?categoryId={cat1Id}");
        Assert.AreEqual(2, res1!.Count);
        Assert.IsTrue(res1.All(p => p.Title == "Paper 1" || p.Title == "Paper 3"));

        // 2. Filter by IsRealExam
        var res2 = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers?isRealExam=true");
        Assert.AreEqual(1, res2!.Count);
        Assert.AreEqual("Paper 1", res2[0].Title);

        // 3. Filter by Tag
        var res3 = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers?tag=Tag1");
        Assert.AreEqual(2, res3!.Count);
        Assert.IsTrue(res3.All(p => p.Title == "Paper 1" || p.Title == "Paper 3"));

        // 4. Combined Filter
        var res4 = await _http.GetFromJsonAsync<List<PaperListDto>>($"/api/Papers?categoryId={cat1Id}&isRealExam=false&tag=Tag1");
        Assert.AreEqual(1, res4!.Count);
        Assert.AreEqual("Paper 3", res4[0].Title);
    }
}
