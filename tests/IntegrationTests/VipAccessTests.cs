using System.Net;
using Aiursoft.DbTools;
using Microsoft.AspNetCore.Identity;
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
    [TestMethod]
    public async Task PracticeNavigationUsesOnlyTotalCategoriesAndTheirOrderedPeriods()
    {
        var course = new TotalCategory { Title = "Navigation course", OrderIndex = 10 };
        var empty = new TotalCategory { Title = "Empty course", OrderIndex = 20 };
        var free = new QuestionCategory { Title = "Free period", TotalCategoryId = course.Id, IsFree = true, OrderIndex = 2 };
        var paid = new QuestionCategory { Title = "Paid period", TotalCategoryId = course.Id, IsFree = false, OrderIndex = 1 };
        var knowledge = new Category { Title = "Knowledge-only overview", ParentId = null };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.AddRange(course, empty, free, paid, knowledge);
            await db.SaveChangesAsync();
        }

        var courses = await _http.GetFromJsonAsync<List<TotalCategorySummaryDto>>("/api/TotalCategories");
        Assert.IsNotNull(courses);
        Assert.IsFalse(courses.Any(c => c.Id == knowledge.Id));
        Assert.AreEqual(course.Title, courses.Single(c => c.Id == course.Id).Title);
        Assert.AreEqual(course.OrderIndex, courses.Single(c => c.Id == course.Id).OrderIndex);
        CollectionAssert.AreEqual(new[] { course.Id, empty.Id },
            courses.Where(c => c.Id == course.Id || c.Id == empty.Id).Select(c => c.Id).ToArray());
        var periods = await _http.GetFromJsonAsync<List<QuestionCategorySummaryDto>>($"/api/TotalCategories/{course.Id}/QuestionCategories");
        Assert.IsNotNull(periods);
        CollectionAssert.AreEqual(new[] { paid.Id, free.Id }, periods.Select(c => c.Id).ToArray());
        Assert.IsTrue(periods.All(c => c.TotalCategoryId == course.Id));
        Assert.AreEqual(paid.Title, periods[0].Title);
        Assert.AreEqual(paid.OrderIndex, periods[0].OrderIndex);
        Assert.IsFalse(periods[0].IsFree);
        Assert.IsFalse(periods[0].HasAccess);
        Assert.IsTrue(periods[1].IsFree);
        Assert.IsTrue(periods[1].HasAccess);
        Assert.AreEqual(0, (await _http.GetFromJsonAsync<List<QuestionCategorySummaryDto>>(
            $"/api/TotalCategories/{empty.Id}/QuestionCategories"))!.Count);
        using var missing = await _http.GetAsync($"/api/TotalCategories/{knowledge.Id}/QuestionCategories");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);
    }

    [TestMethod]
    public async Task PracticeNavigationRequiresTheMatchingActiveCourseMembership()
    {
        var course = new TotalCategory { Title = "Membership course" };
        var other = new TotalCategory { Title = "Other membership course" };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.AddRange(course, other,
                new QuestionCategory { Title = "Paid period", TotalCategoryId = course.Id },
                new QuestionCategory { Title = "Other paid period", TotalCategoryId = other.Id });
            await db.SaveChangesAsync();
        }
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var now = DateTime.UtcNow;
        var product = new VipProduct { Name = "Stopped sales", TotalCategoryId = course.Id, IsEnabled = false };
        var membership = new VipMembership { UserId = "u", VipProduct = product, StartTime = now.AddDays(-1), EndTime = now.AddDays(1) };
        async Task<bool> CanEnter(Guid id) => (await _http.GetFromJsonAsync<List<QuestionCategorySummaryDto>>(
            $"/api/TotalCategories/{id}/QuestionCategories"))!.Single().HasAccess;

        Assert.IsFalse(await CanEnter(course.Id));
        _mockWeChatPayService.Memberships = [membership];
        Assert.IsTrue(await CanEnter(course.Id), "Stopping sales does not revoke a purchased membership.");
        Assert.IsFalse(await CanEnter(other.Id));
        membership.EndTime = now.AddMinutes(-1);
        Assert.IsFalse(await CanEnter(course.Id));
        membership.StartTime = now.AddDays(1);
        membership.EndTime = now.AddDays(2);
        Assert.IsFalse(await CanEnter(course.Id));
        membership.StartTime = now.AddDays(-1);
        product.Type = VipProductType.RealExam;
        product.TotalCategoryId = null;
        Assert.IsFalse(await CanEnter(course.Id), "Real-exam rights do not grant independent practice rights.");
    }


    private async Task<(TotalCategory Course, KnowledgeCategory Directory, KnowledgePoint Lesson, Question Exercise)> AddKnowledgeAsync(bool free)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Knowledge course" };
        var directory = new KnowledgeCategory { Title = "Overview", TotalCategoryId = course.Id, IsFree = free };
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Lesson", Content = "Protected lesson body", AudioUrl = "/protected-audio.mp3" };
        var period = new QuestionCategory { Title = "Period", TotalCategoryId = course.Id };
        var exercise = new Question { Id = Guid.NewGuid(), Content = "Protected exercise", StandardAnswer = " Answer ", QuestionCategory = period };
        db.AddRange(course, directory, lesson, period, exercise,
            new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgeCategoryId = directory.Id, KnowledgePointId = lesson.Id },
            new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = exercise.Id });
        await db.SaveChangesAsync();
        return (course, directory, lesson, exercise);
    }

    [TestMethod]
    public async Task ContentCheckScenariosAgreeWithKnowledgeAndPracticeApis()
    {
        var (course, directory, lesson, exercise) = await AddKnowledgeAsync(false);
        var other = new TotalCategory { Title = "Other knowledge course" };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.TotalCategories.Add(other);
            await db.SaveChangesAsync();
        }
        async Task Compare(string scenarioName)
        {
            using var scope = _server!.Services.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<ContentCheckService>().ScanAsync([other.Id], false);
            var scenario = report.Scenarios.Single(s => s.Name == scenarioName);
            // Include all course choices on demand, not only the exercise's own course.
            report = await scope.ServiceProvider.GetRequiredService<ContentCheckService>()
                .ScanAsync(scenario.Status.ActiveCategoryVips.ToArray(), scenario.Status.HasRealExamVip);
            var access = report.Exercises[exercise.Id].Access.Single(a => a.Scenario ==
                (scenarioName == "Anonymous" ? "Anonymous" : scenarioName == "Signed in without active VIP" ? scenarioName : "Custom VIP selection"));
            using var questionResponse = await _http.GetAsync($"/api/Questions/{exercise.Id}");
            using var knowledgeResponse = await _http.GetAsync($"/api/KnowledgePoints/{lesson.Id}");
            Assert.AreEqual(access.Allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, questionResponse.StatusCode);
            Assert.AreEqual(access.Knowledge.Single().Allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, knowledgeResponse.StatusCode);
        }
        await Compare("Anonymous");
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        await Compare("Signed in without active VIP");
        var product = new VipProduct { Name = "Off-sale course", TotalCategoryId = other.Id, IsEnabled = false };
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", VipProduct = product, StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1)
        }];
        await Compare("Subject VIP: Other knowledge course");
        product.TotalCategoryId = course.Id;
        await Compare("Subject VIP: Knowledge course");
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            (await db.KnowledgeCategories.FindAsync(directory.Id))!.IsFree = true;
            await db.SaveChangesAsync();
        }
        _http.DefaultRequestHeaders.Authorization = null;
        await Compare("Anonymous"); // A pre-existing free-lesson/paid-exercise conflict remains restricted.
    }

    [TestMethod]
    public async Task ContentChecksAgreeWithIndependentPaperApis()
    {
        var course = new TotalCategory { Title = "Paper check course" };
        var school = new Institution { Title = "Paper check school" };
        var otherSchool = new Institution { Title = "Other paper school" };
        var mock = new MockPaper { Title = "Check mock", TotalCategoryId = course.Id, Status = PaperStatus.Publishable };
        var real = new RealPaper { Title = "Check real", InstitutionId = school.Id, Status = PaperStatus.Frozen };
        var mockVersion = new PaperSnapshot { Id = Guid.NewGuid(), Title = mock.Title, MockPaperId = mock.Id, Version = 1 };
        var realVersion = new PaperSnapshot { Id = Guid.NewGuid(), Title = real.Title, RealPaperId = real.Id, Version = 1, IsFree = true };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.AddRange(course, school, otherSchool, mock, real, mockVersion, realVersion,
                new QuestionSnapshot { PaperSnapshotId = mockVersion.Id, Content = "Retained mock" },
                new QuestionSnapshot { PaperSnapshotId = realVersion.Id, Content = "Retained mixed-course paper" });
            await db.SaveChangesAsync();
        }
        async Task Compare(Guid[] subjects, Guid[] institutions, bool global)
        {
            using var scope = _server!.Services.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<ContentCheckService>()
                .ScanAsync(subjects, global, institutions: institutions);
            foreach (var (id, controller, version) in new[] { (mock.Id, "MockPapers", mockVersion.Id), (real.Id, "RealPapers", realVersion.Id) })
            {
                var finding = report.Findings.Single(f => f.Rule == "VIP004" && f.Objects[0].Id == id.ToString());
                var observation = finding.Observations.Single(o => o.Scenario == "Custom VIP selection");
                Assert.AreEqual($"GET /api/{controller}/snapshots/{version}", observation.Entry);
                using var response = await _http.GetAsync($"/api/{controller}/snapshots/{version}");
                Assert.AreEqual(observation.Allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
            }
        }
        await Compare([], [], false);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        await Compare([], [], false);
        var product = new VipProduct { Name = "Off-sale membership", Type = VipProductType.Institution, InstitutionId = otherSchool.Id, IsEnabled = false };
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", VipProduct = product, StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1)
        }];
        await Compare([], [otherSchool.Id], false);
        product.InstitutionId = school.Id;
        await Compare([], [school.Id], false);
        product.Type = VipProductType.Category;
        product.TotalCategoryId = course.Id;
        await Compare([course.Id], [], false);
        product.Type = VipProductType.RealExam;
        await Compare([], [], true);
    }

    [TestMethod]
    public async Task KnowledgeNavigationReturnsOnlyMetadataAndKeepsEveryTreeLevel()
    {
        var (course, directory, lesson, _) = await AddKnowledgeAsync(false);
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var child = new KnowledgeCategory { Title = "Free section", TotalCategoryId = course.Id, ParentKnowledgeCategoryId = directory.Id, IsFree = true };
            var leaf = new KnowledgeCategory { Title = "Paid detail", TotalCategoryId = course.Id, ParentKnowledgeCategoryId = child.Id };
            var other = new TotalCategory { Title = "Another course" };
            db.AddRange(child, leaf, other,
                new KnowledgeCategory { Title = "Excluded", TotalCategoryId = other.Id },
                new KnowledgeCategory { Title = "First", OrderIndex = -1, TotalCategoryId = course.Id, IsFree = true });
            await db.SaveChangesAsync();
        }
        var path = $"/api/TotalCategories/{course.Id}/KnowledgeCategories";
        using var response = await _http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        Assert.IsTrue(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(lesson.Content, body);
        Assert.DoesNotContain(lesson.AudioUrl!, body);
        Assert.DoesNotContain("Excluded", body);
        var tree = (await response.Content.ReadFromJsonAsync<List<KnowledgeCategoryDto>>())!;
        Assert.AreEqual("First", tree[0].Title);
        var root = tree[1];
        Assert.AreEqual(directory.Id, root.Id);
        Assert.AreEqual(course.Id, root.TotalCategoryId);
        Assert.AreEqual(directory.OrderIndex, root.OrderIndex);
        Assert.IsFalse(root.IsFree);
        Assert.AreEqual(1, root.KnowledgePointCount);
        Assert.IsFalse(root.HasAccess);
        Assert.IsTrue(root.Children.Single().HasAccess);
        Assert.IsFalse(root.Children.Single().Children.Single().HasAccess, "A free parent does not unlock a paid child.");
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/TotalCategories/{Guid.NewGuid()}/KnowledgeCategories")).StatusCode);
    }

    [TestMethod]
    public async Task KnowledgeBodiesAndNavigationRequireTheMatchingActiveCourseMembership()
    {
        var (course, directory, lesson, _) = await AddKnowledgeAsync(false);
        async Task Check(bool allowed)
        {
            foreach (var path in new[] { $"/api/KnowledgePoints/{lesson.Id}", $"/api/KnowledgePoints?knowledgeCategoryId={directory.Id}" })
            {
                using var response = await _http.GetAsync(path);
                Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
                Assert.IsTrue(response.Headers.CacheControl?.NoStore);
                var body = await response.Content.ReadAsStringAsync();
                if (allowed) Assert.Contains(lesson.Content, body);
                else
                {
                    Assert.DoesNotContain(lesson.Content, body);
                    Assert.DoesNotContain(lesson.AudioUrl!, body);
                    Assert.Contains(course.Id.ToString(), body);
                }
            }
            var tree = await _http.GetFromJsonAsync<List<KnowledgeCategoryDto>>($"/api/TotalCategories/{course.Id}/KnowledgeCategories");
            Assert.AreEqual(allowed, tree!.Single().HasAccess);
        }
        await Check(false);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        await Check(false);
        var now = DateTime.UtcNow;
        var product = new VipProduct { Name = "Renamed, off-sale membership", Type = VipProductType.Category, TotalCategoryId = Guid.NewGuid(), IsEnabled = false };
        var membership = new VipMembership { UserId = "u", VipProduct = product, StartTime = now.AddDays(-1), EndTime = now.AddDays(1) };
        _mockWeChatPayService.Memberships = [membership];
        await Check(false); // Another course.
        product.TotalCategoryId = course.Id;
        await Check(true); // Neither product name nor sale availability defines purchased rights.
        membership.EndTime = now.AddMinutes(-1);
        await Check(false);
        membership.StartTime = now.AddDays(1); membership.EndTime = now.AddDays(2);
        await Check(false);
        membership.StartTime = now.AddDays(-1); product.Type = VipProductType.RealExam; product.TotalCategoryId = null;
        await Check(false);
    }

    [TestMethod]
    public async Task FreeKnowledgeDoesNotLeakItsPaidExerciseOrGrantIndependentPractice()
    {
        var (course, directory, lesson, paid) = await AddKnowledgeAsync(true);
        var free = new Question { Id = Guid.NewGuid(), QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true), Content = "Free exercise", StandardAnswer = " Free answer " };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.AddRange(free, new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = free.Id });
            await db.SaveChangesAsync();
        }
        var path = $"/api/KnowledgePoints?knowledgeCategoryId={directory.Id}";
        var point = (await _http.GetFromJsonAsync<List<KnowledgePointWithQuestionsDto>>(path))!.Single();
        Assert.AreEqual(lesson.Content, point.Text);
        var locked = point.Questions.Single(q => q.Id == paid.Id);
        Assert.IsFalse(locked.HasAccess);
        Assert.AreEqual(course.Id, locked.RequiredTotalCategoryId);
        Assert.AreEqual(string.Empty, locked.Text);
        Assert.AreEqual(string.Empty, locked.Answer);
        var open = point.Questions.Single(q => q.Id == free.Id);
        Assert.IsTrue(open.HasAccess);
        Assert.AreEqual("Free answer", open.Answer);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{paid.Id}")).StatusCode);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);

        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Course", Type = VipProductType.Category, TotalCategoryId = course.Id }
        }];
        point = (await _http.GetFromJsonAsync<List<KnowledgePointWithQuestionsDto>>(path))!.Single();
        var unlocked = point.Questions.Single(q => q.Id == paid.Id);
        Assert.IsTrue(unlocked.HasAccess);
        Assert.AreEqual(paid.Content, unlocked.Text);
        Assert.AreEqual("Answer", unlocked.Answer);
        using var verification = _server.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(2, context.KnowledgePointQuestions.Count(p => p.KnowledgePointId == lesson.Id), "Reading must not remove conflicting relationships.");
    }

    [TestMethod]
    public async Task KnowledgeDirectLinksNeedAnAccessiblePlacementAndQueriesRejectRetiredFilters()
    {
        var (course, directory, lesson, _) = await AddKnowledgeAsync(false);
        var unpublished = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Unplaced draft", Content = "Private draft body" };
        var free = new KnowledgeCategory { Title = "Free placement", TotalCategoryId = course.Id, IsFree = true };
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.AddRange(unpublished, free,
                new KnowledgeCategoryKnowledgePoint { Id = Guid.NewGuid(), KnowledgeCategoryId = free.Id, KnowledgePointId = lesson.Id });
            await db.SaveChangesAsync();
        }
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/KnowledgePoints/{lesson.Id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/KnowledgePoints?knowledgeCategoryId={directory.Id}")).StatusCode);
        using var draft = await _http.GetAsync($"/api/KnowledgePoints/{unpublished.Id}");
        Assert.AreEqual(HttpStatusCode.Forbidden, draft.StatusCode);
        Assert.DoesNotContain(unpublished.Content, await draft.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/KnowledgePoints/{Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/KnowledgePoints?knowledgeCategoryId={Guid.NewGuid()}")).StatusCode);
        foreach (var path in new[] { "/api/KnowledgePoints", $"/api/KnowledgePoints?categoryId={directory.Id}",
                     $"/api/KnowledgePoints?knowledgeCategoryId={free.Id}&categoryId={directory.Id}" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await _http.GetAsync(path)).StatusCode);
    }

    [TestMethod]
    public async Task KnowledgeApiDoesNotTreatAnAdminCookieAsStudentMembership()
    {
        var (course, directory, lesson, _) = await AddKnowledgeAsync(false);
        using (var scope = _server!.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var admin = new User { UserName = "knowledge_admin", Email = "knowledge-admin@example.com", DisplayName = "Knowledge administrator" };
            Assert.IsTrue((await users.CreateAsync(admin, "Admin@123456!")).Succeeded);
            if (!await roles.RoleExistsAsync("Admin"))
                Assert.IsTrue((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
            Assert.IsTrue((await users.AddToRoleAsync(admin, "Admin")).Succeeded);
        }
        var page = await _http.GetStringAsync("/Account/Login");
        var csrf = System.Text.RegularExpressions.Regex.Match(page,
            @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        Assert.IsFalse(string.IsNullOrEmpty(csrf));
        using var login = await _http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["EmailOrUserName"] = "knowledge_admin", ["Password"] = "Admin@123456!", ["__RequestVerificationToken"] = csrf
        }));
        Assert.AreEqual(HttpStatusCode.Found, login.StatusCode);
        _mockWeChatPayService.Memberships = [new VipMembership
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
            VipProduct = new VipProduct { Name = "Course", Type = VipProductType.Category, TotalCategoryId = course.Id }
        }];
        foreach (var path in new[] { $"/api/KnowledgePoints/{lesson.Id}", $"/api/KnowledgePoints?knowledgeCategoryId={directory.Id}" })
            Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync(path)).StatusCode);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls, "Only a student Bearer identity can load memberships.");
    }

}
