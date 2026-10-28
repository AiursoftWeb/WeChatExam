using System.Net;
using Microsoft.EntityFrameworkCore;
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

    [TestMethod]
    public async Task MockPaperSnapshotsAreNotAvailableThroughLegacyPaperApi()
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var paper = new MockPaper { Title = "Mock publication", TotalCategory = period.TotalCategory!, TotalCategoryId = period.TotalCategoryId };
        var snapshot = new PaperSnapshot { MockPaper = paper, Title = paper.Title, Version = 1, IsFree = true };
        var question = new QuestionSnapshot
        {
            Id = Guid.NewGuid(), PaperSnapshotId = snapshot.Id, PaperSnapshot = snapshot,
            Content = "Mock question", StandardAnswer = "Protected answer", Score = 10,
            GradingStrategy = GradingStrategy.ExactMatch
        };
        var legacy = new PaperSnapshot { Title = "Existing snapshot", Version = 1 };
        var legacyQuestion = new QuestionSnapshot
        {
            Id = Guid.NewGuid(), PaperSnapshotId = legacy.Id, PaperSnapshot = legacy,
            Content = "Existing question", StandardAnswer = "Known answer", Score = 10,
            GradingStrategy = GradingStrategy.ExactMatch
        };
        db.AddRange(question, legacyQuestion);
        await db.SaveChangesAsync();
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/Papers/snapshots/{snapshot.Id}")).StatusCode);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsWeChatUserAsync());
        var rejected = await _http.PostAsJsonAsync("/api/UserPracticeHistory/Snapshot", new CreateUserPracticeHistoryDto { QuestionId = question.Id, UserAnswer = "Guess" });
        Assert.AreEqual(HttpStatusCode.NotFound, rejected.StatusCode);
        Assert.DoesNotContain("Protected answer", await rejected.Content.ReadAsStringAsync());
        var accepted = await _http.PostAsJsonAsync("/api/UserPracticeHistory/Snapshot", new CreateUserPracticeHistoryDto { QuestionId = legacyQuestion.Id, UserAnswer = "Known answer" });
        Assert.AreEqual(HttpStatusCode.OK, accepted.StatusCode);
        Assert.IsTrue((await accepted.Content.ReadFromJsonAsync<UserPracticeHistoryDto>())!.IsCorrect);
        Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
    }

    private async Task<(Guid Paper, Guid Snapshot, Guid Course)> CreatePublishedMock(bool isFree = false)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        period.IsFree = false;
        var paper = new MockPaper { Title = "Published mock", TimeLimit = 180,
            TotalCategory = period.TotalCategory!, TotalCategoryId = period.TotalCategoryId, AllowViewAnonymousely = isFree };
        var question = new Question { Content = "Saved paid-period question", QuestionCategory = period,
            QuestionType = QuestionType.Choice, Metadata = "[options]", StandardAnswer = "Secret answer", Explanation = "Secret explanation" };
        db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaper = paper, Question = question, Order = 2, Score = 15 });
        await db.SaveChangesAsync();
        var snapshot = await scope.ServiceProvider.GetRequiredService<MockPaperPublishing>().PublishAsync(paper);
        return (paper.Id, snapshot.Id, paper.TotalCategoryId);
    }

    [TestMethod]
    public async Task MockAccessFollowsCurrentCourseMembershipAndNeverReturnsAnswers()
    {
        var (paperId, snapshotId, course) = await CreatePublishedMock();
        async Task Check(bool allowed)
        {
            var list = await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>($"/api/MockPapers?totalCategoryId={course}");
            Assert.IsNotNull(list);
            Assert.AreEqual(course, list.Single().TotalCategoryId);
            Assert.IsFalse(list.Single().AllowViewAnonymousely);
            Assert.AreEqual(allowed, list.Single().HasAccess);
            var response = await _http.GetAsync($"/api/MockPapers/snapshots/{snapshotId}");
            Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Secret answer", json);
            Assert.DoesNotContain("Secret explanation", json);
            Assert.DoesNotContain("standardAnswer", json);
            if (allowed)
            {
                var dto = (await response.Content.ReadFromJsonAsync<MockPaperSnapshotDto>())!;
                Assert.AreEqual(paperId, dto.MockPaperId);
                Assert.AreEqual(15, dto.Questions.Single().Score);
                Assert.AreEqual("[options]", dto.Questions.Single().Metadata);
            }
            else
            {
                Assert.DoesNotContain("Saved paid-period question", json);
                Assert.DoesNotContain("[options]", json);
                Assert.Contains(course.ToString(), json);
            }
            Assert.IsTrue(response.Headers.CacheControl!.NoStore);
        }
        await Check(false); // Anonymous.
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsWeChatUserAsync());
        await Check(false); // Logged in without membership.
        foreach (var membership in new[]
        {
            Membership(VipProductType.Category, Guid.NewGuid(), -1, 1),
            Membership(VipProductType.RealExam, null, -1, 1),
            Membership(VipProductType.Category, course, -2, -1),
            Membership(VipProductType.Category, course, 1, 2)
        })
        {
            _mockWeChatPayService.Memberships = [membership];
            await Check(false);
        }
        _mockWeChatPayService.Memberships = [Membership(VipProductType.Category, course, -1, 1)];
        await Check(true);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(0, await db.UserPracticeHistories.CountAsync());
        Assert.AreEqual(0, await db.ExamRecords.CountAsync());
    }

    private static VipMembership Membership(VipProductType type, Guid? course, int startDays, int endDays) => new()
    {
        UserId = "mock-user", StartTime = DateTime.UtcNow.AddDays(startDays), EndTime = DateTime.UtcNow.AddDays(endDays),
        VipProduct = new VipProduct { Name = "Membership", Type = type, TotalCategoryId = course, IsEnabled = false }
    };

    [TestMethod]
    public async Task MockOpeningChangesApplyToAllVersionsWithoutChangingSavedContent()
    {
        var (paperId, firstId, course) = await CreatePublishedMock(true);
        var first = await _http.GetFromJsonAsync<MockPaperSnapshotDto>($"/api/MockPapers/snapshots/{firstId}");
        Assert.AreEqual("Saved paid-period question", first!.Questions.Single().Content);
        Guid secondId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await db.MockPapers.SingleAsync(p => p.Id == paperId);
            paper.Title = "Second published version";
            paper.TimeLimit = 90;
            paper.AllowViewAnonymousely = false;
            (await db.MockPaperQuestions.Include(l => l.Question).SingleAsync(l => l.MockPaperId == paperId)).Question.Content = "Updated live question";
            await db.SaveChangesAsync();
            secondId = (await scope.ServiceProvider.GetRequiredService<MockPaperPublishing>().PublishAsync(paper)).Id;
            paper.Title = "Unpublished draft title";
            paper.TimeLimit = 60;
            await db.SaveChangesAsync();
        }
        var list = await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>($"/api/MockPapers?totalCategoryId={course}");
        Assert.IsNotNull(list);
        Assert.AreEqual("Second published version", list.Single().Title);
        Assert.AreEqual(90, list.Single().TimeLimit);
        Assert.AreEqual(secondId, list.Single().LatestSnapshotId);
        Assert.AreEqual(2, list.Single().LatestVersion);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/MockPapers/snapshots/{firstId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/MockPapers/snapshots/{secondId}")).StatusCode);
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.MockPapers.SingleAsync(p => p.Id == paperId)).AllowViewAnonymousely = true;
            await db.SaveChangesAsync();
        }
        first = await _http.GetFromJsonAsync<MockPaperSnapshotDto>($"/api/MockPapers/snapshots/{firstId}");
        Assert.AreEqual("Saved paid-period question", first!.Questions.Single().Content);
        Assert.AreEqual(180, first.TimeLimit);
        var second = await _http.GetFromJsonAsync<MockPaperSnapshotDto>($"/api/MockPapers/snapshots/{secondId}");
        Assert.IsTrue(second!.AllowViewAnonymousely);
        Assert.AreEqual("Updated live question", second.Questions.Single().Content);
    }

    [TestMethod]
    public async Task MockNavigationSeparatesCoursesDraftsAndLegacySnapshots()
    {
        var (first, firstSnapshot, firstCourse) = await CreatePublishedMock(true);
        var (second, secondSnapshot, secondCourse) = await CreatePublishedMock();
        Guid legacyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.MockPapers.SingleAsync(p => p.Id == first)).Status = PaperStatus.Frozen;
            db.MockPapers.Add(new MockPaper { Title = "Empty draft", TotalCategoryId = firstCourse });
            db.MockPapers.Add(new MockPaper { Title = "No snapshot", TotalCategoryId = firstCourse, Status = PaperStatus.Publishable });
            var legacy = new PaperSnapshot { Title = "Legacy", Version = 1 };
            db.PaperSnapshots.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
        }
        var all = await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>("/api/MockPapers");
        Assert.IsNotNull(all);
        CollectionAssert.AreEqual(new[] { first, second }, all
            .Where(p => p.TotalCategoryId == firstCourse || p.TotalCategoryId == secondCourse)
            .Select(p => p.Id).ToArray());
        var scoped = await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>($"/api/MockPapers?totalCategoryId={secondCourse}");
        Assert.AreEqual(second, scoped!.Single().Id);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/MockPapers?totalCategoryId={Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await _http.GetAsync("/api/MockPapers?totalCategoryId=invalid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/MockPapers/snapshots/{legacyId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/MockPapers/snapshots/{Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/MockPapers/snapshots/{firstSnapshot}")).StatusCode);
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.MockPapers.SingleAsync(p => p.Id == second)).Status = PaperStatus.Draft;
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/MockPapers/snapshots/{secondSnapshot}")).StatusCode);
        Assert.AreEqual(0, (await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>($"/api/MockPapers?totalCategoryId={secondCourse}"))!.Count);
        Assert.AreEqual(first, (await _http.GetFromJsonAsync<List<MockPaperSummaryDto>>($"/api/MockPapers?totalCategoryId={firstCourse}"))!.Single().Id);
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

        async Task AssertAccess(Guid targetSnapshotId, bool isFree, bool hasAccess)
        {
            var list = await _http.GetFromJsonAsync<List<PaperListDto>>("/api/Papers");
            var current = list!.Single(p => p.Id == paperId);
            var snapshot = await _http.GetFromJsonAsync<PaperSnapshotDto>($"/api/Papers/snapshots/{targetSnapshotId}");
            Assert.IsNotNull(snapshot);
            Assert.AreEqual(isFree, current.IsFree);
            Assert.AreEqual(isFree, snapshot.IsFree);
            Assert.AreEqual(hasAccess, current.HasAccess);
            Assert.AreEqual(hasAccess, snapshot.HasAccess);
            Assert.AreEqual(hasAccess ? 1 : 0, snapshot.Questions.Count);
        }

        // An old free snapshot must not bypass the current paid paper's authorization.
        await AssertAccess(snapshotId,false, false);
        var token = await LoginAsWeChatUserAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await AssertAccess(snapshotId,false, false);
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Real", Type = VipProductType.RealExam }
        }];
        await AssertAccess(snapshotId,false, true);

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
        await AssertAccess(snapshotId,false, false);
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Category", Type = VipProductType.Category, TotalCategoryId = categoryId }
        }];
        await AssertAccess(snapshotId,false, true);

        using (var scope = _server.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var paper = await context.Papers.FindAsync(paperId);
            paper!.IsFree = true;
            await context.SaveChangesAsync();
        }
        _mockWeChatPayService.Memberships = [];
        _http.DefaultRequestHeaders.Authorization = null;
        await AssertAccess(snapshotId,true, true);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await AssertAccess(snapshotId,true, true);
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
