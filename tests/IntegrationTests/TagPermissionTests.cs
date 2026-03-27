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
public class TagPermissionTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Mock<IWeChatService> _mockWeChatService;
    private readonly Mock<IWeChatPayService> _mockWeChatPayService;
    private readonly Mock<IDistributionChannelService> _mockDistributionChannelService;

    public TagPermissionTests()
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
            context.Questions.RemoveRange(context.Questions);
            context.Tags.RemoveRange(context.Tags);
            context.Taxonomies.RemoveRange(context.Taxonomies);
            context.Categories.RemoveRange(context.Categories);
            context.VipProducts.RemoveRange(context.VipProducts);
            context.VipMemberships.RemoveRange(context.VipMemberships);
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

    private async Task<(Guid catId, int freeTagId, int paidTagId)> SetupTestDataAsync()
    {
        using var scope = _server!.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();

        // 1. Setup Category
        var cat = new Category { Title = "Test Category", ParentId = null };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        // 2. Setup Taxonomy associated with Category
        var taxonomy = new Taxonomy { Name = "Test Taxonomy" };
        context.Taxonomies.Add(taxonomy);
        await context.SaveChangesAsync();

        context.CategoryTaxonomies.Add(new CategoryTaxonomy { CategoryId = cat.Id, TaxonomyId = taxonomy.Id });
        await context.SaveChangesAsync();

        // 3. Setup Tags
        var freeTag = new Tag { DisplayName = "FreeTag", NormalizedName = "FREETAG", IsFree = true, TaxonomyId = taxonomy.Id };
        var paidTag = new Tag { DisplayName = "PaidTag", NormalizedName = "PAIDTAG", IsFree = false, TaxonomyId = taxonomy.Id };
        context.Tags.Add(freeTag);
        context.Tags.Add(paidTag);
        await context.SaveChangesAsync();

        // 4. Setup Questions
        var q1 = new Question { Content = "Q1", QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch };
        context.Questions.Add(q1);
        await context.SaveChangesAsync();
        context.QuestionTags.Add(new QuestionTag { QuestionId = q1.Id, TagId = freeTag.Id });

        var q2 = new Question { Content = "Q2", QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch };
        context.Questions.Add(q2);
        await context.SaveChangesAsync();
        context.QuestionTags.Add(new QuestionTag { QuestionId = q2.Id, TagId = paidTag.Id });

        await context.SaveChangesAsync();
        return (cat.Id, freeTag.Id, paidTag.Id);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_FreeTag_AlwaysAllowed()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await SetupTestDataAsync();

        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership>());

        var response = await _http.GetAsync("/api/Questions?tagName=FreeTag");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_PaidTag_NoVip_ReturnsForbiddenWithCategoryIds()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var (catId, _, _) = await SetupTestDataAsync();

        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership>());

        var response = await _http.GetAsync("/api/Questions?tagName=PaidTag");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        
        var body = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(body.Contains(catId.ToString()), "Response should contain required category ID");
    }

    [TestMethod]
    public async Task GetQuestionsByTag_PaidTag_WithVip_Allowed()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var (catId, _, _) = await SetupTestDataAsync();

        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership> { 
                new VipMembership { 
                    UserId = "test-user", 
                    StartTime = DateTime.UtcNow.AddDays(-1), 
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "V", Type = VipProductType.Category, CategoryId = catId, IsEnabled = true }
                } 
            });

        var response = await _http.GetAsync("/api/Questions?tagName=PaidTag");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_MultipleCategories_AllowedWithOneVip()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        
        Guid catId1, catId2;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var cat1 = new Category { Title = "Cat 1", ParentId = null };
            var cat2 = new Category { Title = "Cat 2", ParentId = null };
            context.Categories.AddRange(cat1, cat2);
            await context.SaveChangesAsync();
            catId1 = cat1.Id;
            catId2 = cat2.Id;

            var taxonomy = new Taxonomy { Name = "Multi-Cat Taxonomy" };
            context.Taxonomies.Add(taxonomy);
            await context.SaveChangesAsync();

            context.CategoryTaxonomies.Add(new CategoryTaxonomy { CategoryId = catId1, TaxonomyId = taxonomy.Id });
            context.CategoryTaxonomies.Add(new CategoryTaxonomy { CategoryId = catId2, TaxonomyId = taxonomy.Id });
            
            var tag = new Tag { DisplayName = "MultiTag", NormalizedName = "MULTITAG", IsFree = false, TaxonomyId = taxonomy.Id };
            context.Tags.Add(tag);
            await context.SaveChangesAsync();
        }

        // Only have VIP for Cat 2
        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership> { 
                new VipMembership { 
                    UserId = "test-user", 
                    StartTime = DateTime.UtcNow.AddDays(-1), 
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "V2", Type = VipProductType.Category, CategoryId = catId2, IsEnabled = true }
                } 
            });

        var response = await _http.GetAsync("/api/Questions?tagName=MultiTag");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_Mtql_AnyPaidTagDenied_ReturnsForbidden()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await SetupTestDataAsync();

        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership>());

        // MTQL: FreeTag && PaidTag -> 403 because of PaidTag
        var response = await _http.GetAsync("/api/Questions?mtql=FreeTag%20%26%26%20PaidTag");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);

        // MTQL: not PaidTag -> 403 because PaidTag is referenced
        var responseNot = await _http.GetAsync("/api/Questions?mtql=not%20PaidTag");
        Assert.AreEqual(HttpStatusCode.Forbidden, responseNot.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_PaidTag_Uncategorized_Denied()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var tag = new Tag { DisplayName = "OrphanPaidTag", NormalizedName = "ORPHANPAIDTAG", IsFree = false, TaxonomyId = null };
            context.Tags.Add(tag);
            await context.SaveChangesAsync();
        }

        _mockWeChatPayService.Setup(x => x.GetVipStatusListAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<VipMembership>()); // Even with VIP, this tag is unreachable because it has no category

        var response = await _http.GetAsync("/api/Questions?tagName=OrphanPaidTag");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

}
