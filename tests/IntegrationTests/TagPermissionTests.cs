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
    public async Task GetQuestions_Anonymous_FiltersPaidQuestionsBeforePaginationAndRandomSelection()
    {
        var (catId, freeTagId, paidTagId) = await SetupTestDataAsync();
        Guid freeId, untaggedId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var existing = context.Questions.ToList();
            var free = existing.Single(q => q.Content == "Q1");
            var paid = existing.Single(q => q.Content == "Q2");
            freeId = free.Id;
            free.OrderIndex = 1;
            paid.OrderIndex = 4;
            foreach (var question in existing) question.CategoryId = catId;
            var untagged = new Question
            {
                Content = "Untagged", CategoryId = catId, OrderIndex = 2,
                QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch
            };
            var mixed = new Question
            {
                Content = "Mixed", CategoryId = catId, OrderIndex = 3,
                QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch
            };
            context.Questions.AddRange(untagged, mixed);
            await context.SaveChangesAsync();
            untaggedId = untagged.Id;
            context.QuestionTags.AddRange(
                new QuestionTag { QuestionId = mixed.Id, TagId = freeTagId },
                new QuestionTag { QuestionId = mixed.Id, TagId = paidTagId });
            await context.SaveChangesAsync();
        }

        foreach (var query in new[] { "size=10", "randomSize=10", $"categoryId={catId}", "type=0" })
        {
            var questions = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?{query}");
            Assert.IsNotNull(questions);
            CollectionAssert.AreEquivalent(new[] { freeId, untaggedId }, questions.Select(q => q.Id).ToArray());
            Assert.IsTrue(questions.Single(q => q.Id == untaggedId).IsLastQuestion);
            Assert.IsFalse(questions.Single(q => q.Id == freeId).IsLastQuestion);
        }
        foreach (var query in new[] { "tagName=FreeTag", "mtql=FreeTag" })
        {
            var questions = await _http.GetFromJsonAsync<List<QuestionDto>>($"/api/Questions?{query}");
            Assert.IsNotNull(questions);
            Assert.AreEqual(freeId, questions.Single().Id);
            Assert.IsTrue(questions.Single().IsLastQuestion);
        }
        var page = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=1&page=2");
        Assert.IsNotNull(page);
        Assert.AreEqual(untaggedId, page.Single().Id);
        var emptyPage = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=1&page=3");
        Assert.IsNotNull(emptyPage);
        Assert.AreEqual(0, emptyPage.Count);
    }

    [TestMethod]
    public async Task GetQuestion_Anonymous_RestrictsPaidAndMixedTagsButPreservesLoggedInBehavior()
    {
        var (_, freeTagId, paidTagId) = await SetupTestDataAsync();
        Guid freeId, paidId, mixedId, untaggedId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            freeId = context.Questions.Single(q => q.Content == "Q1").Id;
            paidId = context.Questions.Single(q => q.Content == "Q2").Id;
            var mixed = new Question { Content = "Mixed", QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch };
            var untagged = new Question { Content = "Untagged", QuestionType = QuestionType.Choice, GradingStrategy = GradingStrategy.ExactMatch };
            context.Questions.AddRange(mixed, untagged);
            await context.SaveChangesAsync();
            mixedId = mixed.Id;
            untaggedId = untagged.Id;
            context.QuestionTags.AddRange(
                new QuestionTag { QuestionId = mixedId, TagId = freeTagId },
                new QuestionTag { QuestionId = mixedId, TagId = paidTagId });
            await context.SaveChangesAsync();
        }
        foreach (var id in new[] { freeId, untaggedId })
        {
            var response = await _http.GetAsync($"/api/Questions/{id}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var id in new[] { paidId, mixedId })
        {
            var response = await _http.GetAsync($"/api/Questions/{id}");
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var missing = await _http.GetAsync($"/api/Questions/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);

        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var loggedIn = await _http.GetAsync($"/api/Questions/{paidId}");
        Assert.AreEqual(HttpStatusCode.OK, loggedIn.StatusCode);
        var questions = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=10");
        Assert.IsNotNull(questions);
        Assert.AreEqual(4, questions.Count);
    }

    [TestMethod]
    public async Task GetQuestions_Anonymous_PaidTagDeniedAndResumeRequiresLogin()
    {
        await SetupTestDataAsync();
        foreach (var query in new[] { "tagName=PaidTag", "mtql=FreeTag%20%26%26%20PaidTag", "mtql=not%20PaidTag" })
        {
            var response = await _http.GetAsync($"/api/Questions?{query}");
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
        var resume = await _http.GetAsync("/api/Questions?size=10&resumeType=0");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resume.StatusCode);
        Assert.AreEqual(0, _mockWeChatPayService.VipStatusCalls);

        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        var invalidTokenQuestions = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=10");
        Assert.IsNotNull(invalidTokenQuestions);
        Assert.AreEqual("Q1", invalidTokenQuestions.Single().Content);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_FreeTag_AlwaysAllowed()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await SetupTestDataAsync();

        _mockWeChatPayService.Memberships = new List<VipMembership>();

        var response = await _http.GetAsync("/api/Questions?tagName=FreeTag");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_PaidTag_NoVip_ReturnsForbiddenWithCategoryIds()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var (catId, _, _) = await SetupTestDataAsync();

        _mockWeChatPayService.Memberships = new List<VipMembership>();

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

        _mockWeChatPayService.Memberships = new List<VipMembership> {
                new VipMembership {
                    UserId = "test-user",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "V", Type = VipProductType.Category, CategoryId = catId, IsEnabled = true }
                }
            };

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
        _mockWeChatPayService.Memberships = new List<VipMembership> {
                new VipMembership {
                    UserId = "test-user",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "V2", Type = VipProductType.Category, CategoryId = catId2, IsEnabled = true }
                }
            };

        var response = await _http.GetAsync("/api/Questions?tagName=MultiTag");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task GetQuestionsByTag_Mtql_AnyPaidTagDenied_ReturnsForbidden()
    {
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        await SetupTestDataAsync();

        _mockWeChatPayService.Memberships = new List<VipMembership>();

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

        _mockWeChatPayService.Memberships = new List<VipMembership>(); // Even with VIP, this tag is unreachable because it has no category

        var response = await _http.GetAsync("/api/Questions?tagName=OrphanPaidTag");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task GetTags_CategoryVip_GrantsOnlyAssociatedPaidTags()
    {
        var (categoryId, freeTagId, paidTagId) = await SetupTestDataAsync();
        int taxonomyId;
        using (var scope = _server!.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            taxonomyId = context.Tags.Single(t => t.Id == paidTagId).TaxonomyId!.Value;
        }

        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        foreach (var hasMatchingVip in new[] { false, true })
        {
            _mockWeChatPayService.Memberships = new List<VipMembership>
            {
                new()
                {
                    UserId = "test-user",
                    StartTime = DateTime.UtcNow.AddDays(-1),
                    EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct
                    {
                        Name = "Category access",
                        Type = VipProductType.Category,
                        CategoryId = hasMatchingVip ? categoryId : Guid.NewGuid(),
                        IsEnabled = true
                    }
                }
            };
            var tags = await _http.GetFromJsonAsync<List<TagDto>>($"/api/miniprogramapi/taxonomies/{taxonomyId}/tags");
            Assert.IsNotNull(tags);
            Assert.IsTrue(tags.Single(t => t.Id == freeTagId).HasAccess);
            Assert.AreEqual(hasMatchingVip, tags.Single(t => t.Id == paidTagId).HasAccess);
        }
    }

}
