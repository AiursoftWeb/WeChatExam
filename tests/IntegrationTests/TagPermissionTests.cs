using System.Net;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Tests.TestServices;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class TagPermissionTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly StubWeChatService _mockWeChatService;
    private readonly StubWeChatPayService _mockWeChatPayService;
    private readonly StubDistributionChannelService _mockDistributionChannelService;

    public TagPermissionTests()
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
            context.UserPracticeHistories.RemoveRange(context.UserPracticeHistories);
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
        _mockWeChatService.DefaultSession = new WeChatSessionResult { IsSuccess = true, OpenId = openId };

        var response = await _http.PostAsJsonAsync("/api/Auth/login", new Code2SessionDto { Code = "code" });
        var result = await response.Content.ReadFromJsonAsync<TokenDto>();
        return result!.Token;
    }

    private async Task<(Question free, Question paid, Question other)> SeedPracticeAsync()
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Chinese music" };
        var otherCourse = new TotalCategory { Title = "Western music" };
        var freePeriod = new QuestionCategory { Title = "Free period", TotalCategoryId = course.Id, TotalCategory = course, IsFree = true };
        var paidPeriod = new QuestionCategory { Title = "Paid period", TotalCategoryId = course.Id, TotalCategory = course };
        var otherPeriod = new QuestionCategory { Title = "Other period", TotalCategoryId = otherCourse.Id, TotalCategory = otherCourse };
        // Internal tag pricing must not affect practice.
        var paidLabel = new Tag { DisplayName = "Internal paid label", NormalizedName = "INTERNAL PAID LABEL"};
        var free = new Question { Content = "Free question", StandardAnswer = "A", Metadata = "{\"options\":[\"A\",\"B\"]}",
            QuestionCategory = freePeriod, OrderIndex = 1, GradingStrategy = GradingStrategy.ExactMatch };
        var paid = new Question { Content = "Paid untagged question", StandardAnswer = "A", QuestionCategory = paidPeriod, OrderIndex = 2,
            GradingStrategy = GradingStrategy.ExactMatch };
        var other = new Question { Content = "Other course question", StandardAnswer = "A", QuestionCategory = otherPeriod, OrderIndex = 3,
            GradingStrategy = GradingStrategy.ExactMatch };
        db.Questions.AddRange(free, paid, other);
        db.Tags.Add(paidLabel);
        await db.SaveChangesAsync();
        db.QuestionTags.Add(new QuestionTag { QuestionId = free.Id, TagId = paidLabel.Id });
        await db.SaveChangesAsync();
        return (free, paid, other);
    }

    [TestMethod]
    [DataRow("anonymous", false, false)]
    [DataRow("unpaid", false, false)]
    [DataRow("matching", true, false)]
    [DataRow("other", false, true)]
    [DataRow("expired", false, false)]
    [DataRow("future", false, false)]
    [DataRow("real-exam", false, false)]
    [DataRow("off-sale", true, false)]
    public async Task PracticeNavigationListDetailAndGradingUseTheSamePeriodRights(string scenario, bool paidAllowed, bool otherAllowed)
    {
        var (free, paid, other) = await SeedPracticeAsync();
        var course = paid.QuestionCategory!.TotalCategoryId;
        if (scenario != "anonymous")
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        if (scenario is not ("anonymous" or "unpaid"))
            _mockWeChatPayService.Memberships = [new VipMembership
            {
                UserId = "test-user",
                StartTime = DateTime.UtcNow.AddDays(scenario == "future" ? 1 : -2),
                EndTime = DateTime.UtcNow.AddDays(scenario == "expired" ? -1 : 2),
                VipProduct = new VipProduct { Name = "Renamed product", Type = scenario == "real-exam" ? VipProductType.RealExam : VipProductType.Category,
                    TotalCategoryId = scenario == "other" ? other.QuestionCategory!.TotalCategoryId : course, IsEnabled = scenario != "off-sale" }
            }];

        var categories = await _http.GetFromJsonAsync<List<QuestionCategorySummaryDto>>($"/api/TotalCategories/{course}/QuestionCategories");
        Assert.IsNotNull(categories);
        Assert.IsTrue(categories.Single(c => c.Id == free.QuestionCategoryId).HasAccess);
        Assert.AreEqual(paidAllowed, categories.Single(c => c.Id == paid.QuestionCategoryId).HasAccess);
        var expected = new List<Guid> { free.Id };
        if (paidAllowed) expected.Add(paid.Id);
        if (otherAllowed) expected.Add(other.Id);
        foreach (var mode in new[] { "size=50", "randomSize=50", "type=0" })
        {
            var list = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?{mode}");
            Assert.IsNotNull(list);
            CollectionAssert.AreEquivalent(expected, list.Select(q => q.Id).ToList());
            Assert.AreEqual(expected.Last(), list.Single(q => q.IsLastQuestion).Id);
        }
        var courseList = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?totalCategoryId={course}");
        CollectionAssert.AreEquivalent(paidAllowed ? new[] { free.Id, paid.Id } : [free.Id], courseList!.Select(q => q.Id).ToArray());
        var secondPage = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?totalCategoryId={course}&size=1&page=2");
        Assert.AreEqual(paidAllowed ? 1 : 0, secondPage!.Count);
        var periodResponse = await _http.GetAsync($"/api/Questions?questionCategoryId={paid.QuestionCategoryId}");
        Assert.AreEqual(paidAllowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, periodResponse.StatusCode);

        var accepted = 0;
        foreach (var (question, allowed) in new[] { (free, true), (paid, paidAllowed), (other, otherAllowed) })
        {
            var detail = await _http.GetAsync($"/api/Questions/{question.Id}");
            Assert.AreEqual(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, detail.StatusCode);
            if (allowed)
            {
                var body = await detail.Content.ReadFromJsonAsync<QuestionDto>();
                Assert.AreEqual(question.Content, body!.Content);
                Assert.AreEqual(question.Metadata, body.Metadata);
            }
            var answer = await _http.PostAsJsonAsync("/api/UserPracticeHistory", new CreateUserPracticeHistoryDto
            {
                QuestionId = question.Id, UserAnswer = "A", PracticeType = PracticeType.QuestionType
            });
            var expectedStatus = scenario == "anonymous" ? HttpStatusCode.Unauthorized : allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden;
            Assert.AreEqual(expectedStatus, answer.StatusCode);
            if (expectedStatus == HttpStatusCode.Created)
            {
                accepted++;
                var graded = await answer.Content.ReadFromJsonAsync<UserPracticeHistoryDto>();
                Assert.AreEqual(true, graded!.IsCorrect);
                Assert.AreEqual("A", graded.StandardAnswer);
            }
            else
                Assert.IsFalse((await answer.Content.ReadAsStringAsync()).Contains("standardAnswer"));
        }
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(accepted, db.UserPracticeHistories.Count(), "Denied submissions must not write practice history.");
    }

    [TestMethod]
    public async Task PracticeFiltersRejectLegacyNavigationAndMismatchedCourses()
    {
        var (_, paid, other) = await SeedPracticeAsync();
        foreach (var query in new[] { "categoryId=" + Guid.NewGuid(), "tagName=Internal", "mtql=anything", "size=0", "randomSize=-1", "page=0",
                     $"totalCategoryId={other.QuestionCategory!.TotalCategoryId}&questionCategoryId={paid.QuestionCategoryId}" })
            Assert.AreEqual(HttpStatusCode.BadRequest, (await _http.GetAsync($"/api/Questions?{query}")).StatusCode, query);
        foreach (var query in new[] { $"totalCategoryId={Guid.NewGuid()}", $"questionCategoryId={Guid.NewGuid()}" })
            Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/Questions?{query}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/Questions/{Guid.NewGuid()}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await _http.GetAsync("/api/Questions?resumeType=0&size=10")).StatusCode);
    }

    [TestMethod]
    public async Task ChangingPeriodPriceRevokesContentAndGradingEvenAfterNavigationWasLoaded()
    {
        var (free, _, _) = await SeedPracticeAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        var navigation = await _http.GetFromJsonAsync<List<QuestionCategorySummaryDto>>($"/api/TotalCategories/{free.QuestionCategory!.TotalCategoryId}/QuestionCategories");
        Assert.IsTrue(navigation!.Single(c => c.Id == free.QuestionCategoryId).HasAccess);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.QuestionCategories.Single(c => c.Id == free.QuestionCategoryId).IsFree = false;
        await db.SaveChangesAsync();
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{free.Id}")).StatusCode);
        var list = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?randomSize=50");
        Assert.AreEqual(0, list!.Count);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.PostAsJsonAsync("/api/UserPracticeHistory",
            new CreateUserPracticeHistoryDto { QuestionId = free.Id, UserAnswer = "A" })).StatusCode);
        Assert.AreEqual(0, db.UserPracticeHistories.Count());
    }

    [TestMethod]
    public async Task ResumeIgnoresHistoryFromAnotherPeriod()
    {
        var (free, _, other) = await SeedPracticeAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await LoginAsync());
        Guid nextId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var next = new Question { Content = "Next free question", QuestionCategoryId = free.QuestionCategoryId, OrderIndex = 2 };
            db.Questions.Add(next);
            nextId = next.Id;
            var user = db.Users.Single(u => u.MiniProgramOpenId == "test-user").Id;
            db.UserPracticeHistories.AddRange(
                new UserPracticeHistory { UserId = user, QuestionId = free.Id, UserAnswer = "A", IsCorrect = true,
                    PracticeType = PracticeType.QuestionType, CreationTime = DateTime.UtcNow.AddMinutes(-1) },
                new UserPracticeHistory { UserId = user, QuestionId = other.Id, UserAnswer = "A", IsCorrect = true,
                    PracticeType = PracticeType.QuestionType, CreationTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var list = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?questionCategoryId={free.QuestionCategoryId}&resumeType=0&size=10");
        Assert.AreEqual(nextId, list!.Single().Id);
    }

}
