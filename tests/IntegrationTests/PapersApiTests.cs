using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PapersApiTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService;
    private readonly StubWeChatPayService _mockWeChatPayService;
    private readonly StubDistributionChannelService _mockDistributionChannelService;

    public PapersApiTests()
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
        _mockWeChatPayService.Memberships = new List<VipMembership>();
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
    public async Task GetSnapshot_UsesCurrentPaperAuthorizationAfterChanges()
    {
        Guid paperId, snapshotId, categoryId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IPaperService>();
            var category = new Category { Title = "Snapshot access", ParentId = null };
            context.Categories.Add(category);
            await context.SaveChangesAsync();
            categoryId = category.Id;
            var paper = await service.CreatePaperAsync("Changing access", 60, true, true);
            paperId = paper.Id;
            await service.AssociateCategoryAsync(paperId, categoryId);
            await service.SetStatusAsync(paperId, PaperStatus.Publishable);
            var snapshot = await service.PublishAsync(paperId);
            snapshotId = snapshot.Id;
            context.QuestionSnapshots.Add(new QuestionSnapshot
            {
                PaperSnapshotId = snapshotId, Content = "Original snapshot content",
                QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch
            });
            paper.IsFree = false;
            await context.SaveChangesAsync();
        }

        async Task AssertAccess(bool isFree, bool hasAccess)
        {
            var list = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
            var current = list!.Single(p => p.Id == paperId);
            var snapshot = await _http.GetFromJsonAsync<PaperSnapshotDto>($"/api/Papers/snapshots/{snapshotId}");
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(isFree, current.IsFree);
            Assert.AreEqual(isFree, snapshot.IsFree);
            Assert.AreEqual(hasAccess, current.HasAccess);
            Assert.AreEqual(hasAccess, snapshot.HasAccess);
            Assert.AreEqual(hasAccess ? 1 : 0, snapshot.Questions.Count);
        }

        // An old free snapshot must not bypass the current paid paper's authorization.
        await AssertAccess(false, false);
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await AssertAccess(false, false);
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Real", Type = VipProductType.RealExam }
        }];
        await AssertAccess(false, true);

        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await context.Papers.FindAsync(paperId);
            paper!.IsRealExam = false;
            await context.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<IPaperService>();
            var paidSnapshot = await service.PublishAsync(paperId);
            snapshotId = paidSnapshot.Id;
            context.QuestionSnapshots.Add(new QuestionSnapshot
            {
                PaperSnapshotId = snapshotId, Content = "Original snapshot content",
                QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch
            });
            await context.SaveChangesAsync();
        }
        await AssertAccess(false, false);
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Category", Type = VipProductType.Category, CategoryId = categoryId }
        }];
        await AssertAccess(false, true);

        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await context.Papers.FindAsync(paperId);
            paper!.IsFree = true;
            await context.SaveChangesAsync();
        }
        _mockWeChatPayService.Memberships = [];
        _http.DefaultRequestHeaders.Authorization = null;
        await AssertAccess(true, true);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await AssertAccess(true, true);
    }

    [TestMethod]
    public async Task GetPapers_DefaultOrdering_IsFreeThenTitleThenId()
    {
        var freeAFirst = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var freeASecond = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var freeZ = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var paidAFirst = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var paidASecond = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var paidZ = Guid.Parse("00000000-0000-0000-0000-000000000006");
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IPaperService>();
            var category = new Category { Title = "Ordering category", ParentId = null };
            context.Categories.Add(category);
            var papers = new[]
            {
                new Paper { Id = paidZ, Title = "Z paper", IsFree = false },
                new Paper { Id = freeASecond, Title = "A paper", IsFree = true },
                new Paper { Id = paidASecond, Title = "A paper", IsFree = false },
                new Paper { Id = freeZ, Title = "Z paper", IsFree = true },
                new Paper { Id = paidAFirst, Title = "A paper", IsFree = false },
                new Paper { Id = freeAFirst, Title = "A paper", IsFree = true }
            };
            context.Papers.AddRange(papers);
            await context.SaveChangesAsync();
            foreach (var paper in papers)
            {
                await service.AssociateCategoryAsync(paper.Id, category.Id);
                await service.SetStatusAsync(paper.Id, PaperStatus.Publishable);
                await service.PublishAsync(paper.Id);
            }

            var expected = new[] { freeAFirst, freeASecond, freeZ, paidAFirst, paidASecond, paidZ };
            foreach (var url in new[] { "/api/Papers", $"/api/Papers?categoryId={category.Id}&isRealExam=false" })
            {
                var result = await _http.GetFromJsonAsync<List<PaperListDto>>(url);
                Assert.IsNotNull(result);
                CollectionAssert.AreEqual(expected, result.Select(p => p.Id).ToArray());
            }
        }
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
