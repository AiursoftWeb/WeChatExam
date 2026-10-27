using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class VipAccessTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService;
    private readonly StubWeChatPayService _mockWeChatPayService;
    private readonly StubDistributionChannelService _mockDistributionChannelService;

    public VipAccessTests()
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
        _mockWeChatService.DefaultSession = new WeChatSessionResult { IsSuccess = true, OpenId = openId };

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = "code" });
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
    }

    [TestMethod]
    public async Task ContentApis_Anonymous_CanReadFreeContentButNotPaidQuestions()
    {
        Guid freePaperId;
        Guid paidPaperId;
        int taxonomyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paperService = scope.ServiceProvider.GetRequiredService<IPaperService>();
            var freePaper = await paperService.CreatePaperAsync("Free", 60, true);
            var paidPaper = await paperService.CreatePaperAsync("Paid", 60, false, true);
            freePaperId = freePaper.Id;
            paidPaperId = paidPaper.Id;
            foreach (var paper in new[] { freePaper, paidPaper })
            {
                await paperService.SetStatusAsync(paper.Id, PaperStatus.Publishable);
                var snapshot = await paperService.PublishAsync(paper.Id);
                context.QuestionSnapshots.Add(new QuestionSnapshot
                {
                    PaperSnapshotId = snapshot.Id,
                    Content = "Snapshot question",
                    QuestionType = QuestionType.Choice,
                    GradingStrategy = GradingStrategy.ExactMatch
                });
            }

            var taxonomy = new Taxonomy { Name = "Anonymous taxonomy" };
            context.Taxonomies.Add(taxonomy);
            await context.SaveChangesAsync();
            taxonomyId = taxonomy.Id;
            context.Tags.AddRange(
                new Tag { DisplayName = "Free", NormalizedName = "FREE", IsFree = true, TaxonomyId = taxonomyId },
                new Tag { DisplayName = "Paid", NormalizedName = "PAID", IsFree = false, TaxonomyId = taxonomyId });
            await context.SaveChangesAsync();
        }

        var papers = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsNotNull(papers);
        var free = papers.Single(p => p.Id == freePaperId);
        var paid = papers.Single(p => p.Id == paidPaperId);
        Assert.IsTrue(free.HasAccess);
        Assert.IsFalse(paid.HasAccess);

        var versions = await _http.GetFromJsonAsync<List<SnapshotListDto>>($"/api/Papers/{freePaperId}/snapshots");
        Assert.IsNotNull(versions);
        Assert.AreEqual(1, versions.Count);
        var freeSnapshot = await _http.GetFromJsonAsync<PaperSnapshotDto>($"/api/Papers/snapshots/{free.LatestSnapshotId}");
        Assert.IsNotNull(freeSnapshot);
        Assert.IsTrue(freeSnapshot.HasAccess);
        Assert.AreEqual(1, freeSnapshot.Questions.Count);
        var paidSnapshot = await _http.GetFromJsonAsync<PaperSnapshotDto>($"/api/Papers/snapshots/{paid.LatestSnapshotId}");
        Assert.IsNotNull(paidSnapshot);
        Assert.IsFalse(paidSnapshot.HasAccess);
        Assert.AreEqual(0, paidSnapshot.Questions.Count);

        var taxonomies = await _http.GetFromJsonAsync<List<TaxonomyDto>>("/api/miniprogramapi/taxonomies");
        Assert.IsNotNull(taxonomies);
        Assert.IsTrue(taxonomies.Any(t => t.Id == taxonomyId));
        var tags = await _http.GetFromJsonAsync<List<TagDto>>($"/api/miniprogramapi/taxonomies/{taxonomyId}/tags");
        Assert.IsNotNull(tags);
        Assert.IsTrue(tags.Single(t => t.IsFree).HasAccess);
        Assert.IsFalse(tags.Single(t => !t.IsFree).HasAccess);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);

        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        _mockWeChatPayService.Memberships = new List<VipMembership>
            {
                new VipMembership
                {
                    UserId = "u",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "Real", Type = VipProductType.RealExam }
                }
            };
        var vipSnapshot = await _http.GetFromJsonAsync<PaperSnapshotDto>($"/api/Papers/snapshots/{paid.LatestSnapshotId}");
        Assert.IsNotNull(vipSnapshot);
        Assert.IsTrue(vipSnapshot.HasAccess);
        Assert.AreEqual(1, vipSnapshot.Questions.Count);
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
        _mockWeChatPayService.Memberships = new List<VipMembership>();

        var resA = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsTrue(resA!.First(p => p.Title == "Free").HasAccess);
        Assert.IsFalse(resA!.First(p => p.Title == "Real").HasAccess);
        Assert.IsFalse(resA!.First(p => p.Title == "Cat Paper").HasAccess);

        // Scenario B: Has Real Exam VIP
        _mockWeChatPayService.Memberships = new List<VipMembership> {
                new VipMembership {
                    UserId = "u",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "R", Type = VipProductType.RealExam }
                }
            };

        var resB = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsTrue(resB!.First(p => p.Title == "Real").HasAccess);
        Assert.IsFalse(resB!.First(p => p.Title == "Cat Paper").HasAccess);

        // Scenario C: Has Category VIP
        _mockWeChatPayService.Memberships = new List<VipMembership> {
                new VipMembership {
                    UserId = "u",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "C", Type = VipProductType.Category, TotalCategoryId = catId }
                }
            };

        var resC = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
        Assert.IsFalse(resC!.First(p => p.Title == "Real").HasAccess);
        Assert.IsTrue(resC!.First(p => p.Title == "Cat Paper").HasAccess);
    }
}
