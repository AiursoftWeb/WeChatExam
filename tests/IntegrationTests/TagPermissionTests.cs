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
    public async Task GetQuestion_RestrictsPaidAndMixedTagsWithoutVip()
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
        Assert.AreEqual(HttpStatusCode.Forbidden, loggedIn.StatusCode);
        foreach (var id in new[] { freeId, untaggedId })
        {
            Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/Questions/{id}")).StatusCode);
        }
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{mixedId}")).StatusCode);
        var questions = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=10");
        Assert.IsNotNull(questions);
        Assert.AreEqual(4, questions.Count);
    }

    [TestMethod]
    public async Task GetQuestion_SubjectVipUnlocksPaidQuestionsRegardlessOfDescriptiveTags()
    {
        var (subjectId, freeTagId, _) = await SetupTestDataAsync();
        Guid questionId;
        const string metadata = "{\"options\":[\"A\",\"B\"]}";
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var genre = new Taxonomy { Name = "Musical genres" };
            var works = new Taxonomy { Name = "Works and books" };
            db.AddRange(genre, works);
            await db.SaveChangesAsync();
            // Shared descriptive taxonomies need no subject mapping to grant subject VIP access.
            var genreTag = new Tag { DisplayName = "Dance music", NormalizedName = "DANCE MUSIC", IsFree = false, TaxonomyId = genre.Id };
            var workTag = new Tag { DisplayName = "Ancient work", NormalizedName = "ANCIENT WORK", IsFree = false, TaxonomyId = works.Id };
            var question = new Question { Content = "Subject exercise", CategoryId = subjectId, Metadata = metadata };
            db.AddRange(genreTag, workTag, question);
            await db.SaveChangesAsync();
            questionId = question.Id;
            db.QuestionTags.AddRange(
                new QuestionTag { QuestionId = questionId, TagId = genreTag.Id },
                new QuestionTag { QuestionId = questionId, TagId = workTag.Id },
                new QuestionTag { QuestionId = questionId, TagId = freeTagId });
            // A tag mapped to a different subject must neither replace nor add to the question's subject.
            var otherSubject = new Category { Title = "Other subject", ParentId = null };
            db.Categories.Add(otherSubject);
            await db.SaveChangesAsync();
            db.Questions.Single(q => q.Content == "Q2").CategoryId = otherSubject.Id;
            await db.SaveChangesAsync();
        }

        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{questionId}")).StatusCode);
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "invalid-token");
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{questionId}")).StatusCode);
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        VipMembership Membership(Guid? category, int startDays = -1, int endDays = 1) => new()
        {
            UserId = "test-user", StartTime = DateTime.UtcNow.AddDays(startDays), EndTime = DateTime.UtcNow.AddDays(endDays),
            VipProduct = new VipProduct { Name = "Subject subscription", Type = category.HasValue ? VipProductType.Category : VipProductType.RealExam, CategoryId = category }
        };

        _mockWeChatPayService.Memberships = [Membership(subjectId)];
        var allowed = await _http.GetAsync($"/api/Questions/{questionId}");
        Assert.AreEqual(HttpStatusCode.OK, allowed.StatusCode, "A subject VIP includes exercises with unmapped paid descriptive tags.");
        var dto = await allowed.Content.ReadFromJsonAsync<QuestionDto>();
        Assert.AreEqual(metadata, dto!.Metadata);
        Assert.AreEqual(questionId, dto.Id);

        Guid otherQuestionId, otherSubjectId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var otherQuestion = db.Questions.Single(q => q.Content == "Q2");
            otherQuestionId = otherQuestion.Id;
            otherSubjectId = otherQuestion.CategoryId!.Value;
        }
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{otherQuestionId}")).StatusCode);
        _mockWeChatPayService.Memberships = [Membership(otherSubjectId)];
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/Questions/{otherQuestionId}")).StatusCode,
            "The question's own subject VIP suffices even when its paid tag maps to another subject.");

        foreach (var memberships in new List<VipMembership>[]
                 {
                     [], [Membership(Guid.NewGuid())], [Membership(subjectId, -2, -1)],
                     [Membership(subjectId, 1, 2)], [Membership(null)]
                 })
        {
            _mockWeChatPayService.Memberships = memberships;
            var denied = await _http.GetAsync($"/api/Questions/{questionId}");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            var body = System.Text.Json.Nodes.JsonNode.Parse(await denied.Content.ReadAsStringAsync())!;
            CollectionAssert.AreEqual(new[] { subjectId }, body["requiredCategoryIds"]!.AsArray().Select(id => id!.GetValue<Guid>()).ToArray());
        }
    }

    [TestMethod]
    public async Task GetQuestion_PaidTagsRequireActiveCategoryVipForEveryTag()
    {
        var (catId, _, paidTagId) = await SetupTestDataAsync();
        Guid paidId, secondCategoryId, alternativeCategoryId, mixedId, orphanId, unmappedId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            paidId = db.Questions.Single(q => q.Content == "Q2").Id;
            var secondCategory = new Category { Title = "Second category", ParentId = null };
            var alternativeCategory = new Category { Title = "Alternative category", ParentId = null };
            var secondTaxonomy = new Taxonomy { Name = "Second taxonomy" };
            var unmappedTaxonomy = new Taxonomy { Name = "No category mapping" };
            db.AddRange(secondCategory, alternativeCategory, secondTaxonomy, unmappedTaxonomy);
            await db.SaveChangesAsync();
            secondCategoryId = secondCategory.Id;
            alternativeCategoryId = alternativeCategory.Id;
            var originalTaxonomyId = db.Tags.Single(t => t.Id == paidTagId).TaxonomyId!.Value;
            db.CategoryTaxonomies.AddRange(
                new CategoryTaxonomy { CategoryId = alternativeCategory.Id, TaxonomyId = originalTaxonomyId },
                new CategoryTaxonomy { CategoryId = secondCategoryId, TaxonomyId = secondTaxonomy.Id });
            var secondTag = new Tag { DisplayName = "Second paid", NormalizedName = "SECOND PAID", IsFree = false, TaxonomyId = secondTaxonomy.Id };
            var orphanTag = new Tag { DisplayName = "Orphan", NormalizedName = "ORPHAN", IsFree = false, TaxonomyId = null };
            var unmappedTag = new Tag { DisplayName = "Unmapped", NormalizedName = "UNMAPPED", IsFree = false, TaxonomyId = unmappedTaxonomy.Id };
            var mixed = new Question { Content = "Two paid tags" };
            var orphan = new Question { Content = "Orphan tag question" };
            var unmapped = new Question { Content = "Unmapped tag question" };
            db.AddRange(secondTag, orphanTag, unmappedTag, mixed, orphan, unmapped);
            await db.SaveChangesAsync();
            mixedId = mixed.Id; orphanId = orphan.Id; unmappedId = unmapped.Id;
            db.QuestionTags.AddRange(
                new QuestionTag { QuestionId = mixedId, TagId = paidTagId },
                new QuestionTag { QuestionId = mixedId, TagId = secondTag.Id },
                new QuestionTag { QuestionId = orphanId, TagId = orphanTag.Id },
                new QuestionTag { QuestionId = unmappedId, TagId = unmappedTag.Id });
            await db.SaveChangesAsync();
        }
        var token = await LoginAsync();
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        VipMembership Membership(Guid? category, int startDays = -1, int endDays = 1) => new()
        {
            UserId = "u", StartTime = DateTime.UtcNow.AddDays(startDays), EndTime = DateTime.UtcNow.AddDays(endDays),
            VipProduct = new VipProduct { Name = "Access test", Type = category.HasValue ? VipProductType.Category : VipProductType.RealExam, CategoryId = category }
        };
        foreach (var membership in new[]
                 {
                     Membership(Guid.NewGuid()), Membership(catId, -2, -1),
                     Membership(catId, 1, 2), Membership(null)
                 })
        {
            _mockWeChatPayService.Memberships = [membership];
            var denied = await _http.GetAsync($"/api/Questions/{paidId}");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            var body = System.Text.Json.Nodes.JsonNode.Parse(await denied.Content.ReadAsStringAsync())!;
            Assert.IsTrue(body["requiredCategoryIds"]!.AsArray().Any(id => id!.GetValue<Guid>() == catId));
        }
        _mockWeChatPayService.Memberships = [Membership(alternativeCategoryId)];
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/Questions/{paidId}")).StatusCode);
        _mockWeChatPayService.Memberships = [Membership(catId)];
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/Questions/{paidId}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/Questions/{mixedId}")).StatusCode);
        _mockWeChatPayService.Memberships.Add(Membership(secondCategoryId));
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/Questions/{mixedId}")).StatusCode);
        foreach (var id in new[] { orphanId, unmappedId })
        {
            var denied = await _http.GetAsync($"/api/Questions/{id}");
            Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            var body = System.Text.Json.Nodes.JsonNode.Parse(await denied.Content.ReadAsStringAsync())!;
            Assert.AreEqual(0, body["requiredCategoryIds"]!.AsArray().Count);
        }
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

    [TestMethod]
    public async Task DiagnosticScenariosMatchActualDetailResponsesAndExposeListDifference()
    {
        var (subject, _, _) = await SetupTestDataAsync();
        Guid questionId;
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var question = db.Questions.Single(q => q.Content == "Q2");
            question.CategoryId = subject;
            questionId = question.Id;
            await db.SaveChangesAsync();
        }
        using var reportScope = _server!.Services.CreateScope();
        var report = await reportScope.ServiceProvider.GetRequiredService<ContentCheckService>().ScanAsync([subject], false);
        var finding = report.Findings.Single(f => f.Rule == "VIP002");
        var token = await LoginAsync();
        foreach (var scenario in report.Scenarios)
        {
            _mockWeChatPayService.Memberships.Clear();
            foreach (var id in scenario.Status.ActiveCategoryVips)
                _mockWeChatPayService.Memberships.Add(new VipMembership
                {
                    UserId = "test-user", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "Subject", CategoryId = id }
                });
            if (scenario.Status.HasRealExamVip)
                _mockWeChatPayService.Memberships.Add(new VipMembership
                {
                    UserId = "test-user", StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(1),
                    VipProduct = new VipProduct { Name = "Real", Type = VipProductType.RealExam }
                });
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/Questions/{questionId}");
            if (scenario.Authenticated) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var actual = await _http.SendAsync(request);
            var expected = finding.Observations.Single(o => o.Scenario == scenario.Name).Allowed;
            Assert.AreEqual(expected ? HttpStatusCode.OK : HttpStatusCode.Forbidden, actual.StatusCode, scenario.Name);
        }
        _mockWeChatPayService.Memberships.Clear();
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/Questions?size=50");
        listRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var listResponse = await _http.SendAsync(listRequest);
        var listed = await listResponse.Content.ReadFromJsonAsync<List<QuestionDto>>();
        Assert.IsTrue(listed!.Any(q => q.Id == questionId));
        Assert.IsTrue(report.Findings.Any(f => f.Rule == "API001" && f.NeedsEngineering));
        var anonymousList = await _http.GetFromJsonAsync<List<QuestionDto>>("/api/Questions?size=50");
        Assert.IsFalse(anonymousList!.Any(q => q.Id == questionId));
    }

}
