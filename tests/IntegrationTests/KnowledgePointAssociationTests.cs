using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Tests.TestServices;
using Microsoft.EntityFrameworkCore;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class KnowledgePointAssociationTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Guid _courseId = Guid.NewGuid();

    public KnowledgePointAssociationTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _http = new HttpClient(handler);
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        TestStartupWithMockWeChat.MockVipProductService = null;
        TestStartupWithMockWeChat.MockWeChatService = new StubWeChatService();
        TestStartupWithMockWeChat.MockWeChatPayService = new StubWeChatPayService();
        TestStartupWithMockWeChat.MockDistributionChannelService = new StubDistributionChannelService();
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<TestStartupWithMockWeChat>(["--applicationName", "Aiursoft.WeChatExam"], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(_server);
            await _server.UpdateDbAsync<WeChatExamDbContext>();
            await _server.SeedAsync();
            using (var scope = _server.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
                db.TotalCategories.Add(new TotalCategory { Id = _courseId, Title = "Test course" });
                await db.SaveChangesAsync();
            }
            await _server.StartAsync();
        }
        catch
        {
            _server.Dispose();
            throw;
        }
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    private async Task LoginAsAdminAsync()
    {
        var email = "admin@default.com"; 
        var password = "Admin@123456!";

        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
    }

    [TestMethod]
    public async Task KnowledgeContentRemainsInItsCategoryWithoutASecondHierarchy()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Music history" };
        var chapter = new KnowledgeCategory { Title = "Early music", TotalCategoryId = course.Id, IsFree = true };
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true), Id = Guid.NewGuid(), Content = "Related exercise" };
        db.AddRange(course, chapter, question);
        await db.SaveChangesAsync();

        var createHtml = await _http.GetStringAsync("/KnowledgePoints/Create");
        Assert.DoesNotContain("name=\"ParentId\"", createHtml);
        var response = await _http.PostAsync("/KnowledgePoints/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Overview",
            ["Content"] = "Original lesson content",
            ["SelectedKnowledgeCategoryIds[0]"] = chapter.Id.ToString(),
            ["SelectedQuestionIds[0]"] = question.Id.ToString(),
            ["__RequestVerificationToken"] = await GetAntiCsrfToken("/KnowledgePoints/Create")
        }));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var id = Guid.Parse(response.Headers.Location!.OriginalString.Split('/').Last());
        var editUrl = $"/KnowledgePoints/Edit/{id}";
        var editHtml = await _http.GetStringAsync(editUrl);
        Assert.DoesNotContain("name=\"ParentId\"", editHtml);

        // Validation errors must still show the editor and its category/exercise choices.
        var fields = new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Title"] = "", ["Content"] = "Updated lesson content",
            ["SelectedKnowledgeCategoryIds[0]"] = chapter.Id.ToString(),
            ["SelectedQuestionIds[0]"] = question.Id.ToString(),
            ["__RequestVerificationToken"] = await GetAntiCsrfToken(editUrl)
        };
        var invalid = await _http.PostAsync(editUrl, new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains(chapter.Title, invalidHtml);
        Assert.DoesNotContain("name=\"ParentId\"", invalidHtml);
        fields["Title"] = "Updated overview";
        fields["__RequestVerificationToken"] = await GetAntiCsrfToken(editUrl);
        var edited = await _http.PostAsync(editUrl, new FormUrlEncodedContent(fields));
        Assert.AreEqual(HttpStatusCode.Found, edited.StatusCode);

        var content = await _http.GetStringAsync($"/api/KnowledgePoints?knowledgeCategoryId={chapter.Id}");
        Assert.Contains("Updated lesson content", content);
        Assert.Contains(question.Content, content);
        var list = await _http.GetStringAsync("/KnowledgePoints");
        Assert.Contains("Updated overview", list);
        Assert.Contains($"/KnowledgeCategories/Details/{chapter.Id}", list);
        Assert.DoesNotContain("tree-tab", list);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync("/api/KnowledgePoints/all")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/KnowledgePoints/all/{id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync($"/api/KnowledgePoints/{id}")).StatusCode);

        var deletion = await _http.PostAsync($"/KnowledgePoints/Delete/{id}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await GetAntiCsrfToken($"/KnowledgePoints/Delete/{id}")
        }));
        Assert.AreEqual(HttpStatusCode.Found, deletion.StatusCode);
        db.ChangeTracker.Clear();
        Assert.IsFalse(await db.KnowledgePoints.AnyAsync(p => p.Id == id));
        Assert.AreEqual(course.Id, (await db.KnowledgeCategories.FindAsync(chapter.Id))!.TotalCategoryId);
        Assert.IsNotNull(await db.Questions.FindAsync(question.Id));
    }

    [TestMethod]
    public async Task TestKnowledgePointCategoryAssociation()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = "Test Association Category";
        var createCatToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "TotalCategoryId", _courseId.ToString() },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/KnowledgeCategories/Create", createCatContent);
        Assert.AreEqual(HttpStatusCode.Found, createCatResponse.StatusCode);
        var categoryId = createCatResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create a Knowledge Point
        var kpTitle = "Test Association KP";
        var kpContent = "Test KP Content";
        var createKpToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", kpTitle },
            { "Content", kpContent },
            { "__RequestVerificationToken", createKpToken }
        });
        var createKpResponse = await _http.PostAsync("/KnowledgePoints/Create", createKpContent);
        Assert.AreEqual(HttpStatusCode.Found, createKpResponse.StatusCode);
        var kpId = createKpResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Associate them via Knowledge Point Edit
        var editKpToken = await GetAntiCsrfToken($"/KnowledgePoints/Edit/{kpId}");
        var editKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", kpId },
            { "Title", kpTitle },
            { "Content", kpContent },
            { "SelectedKnowledgeCategoryIds[0]", categoryId },
            { "__RequestVerificationToken", editKpToken }
        });
        var editKpResponse = await _http.PostAsync($"/KnowledgePoints/Edit/{kpId}", editKpContent);
        Assert.AreEqual(HttpStatusCode.Found, editKpResponse.StatusCode);

        // 4. Verify Association in Knowledge Point Details
        var kpDetailsResponse = await _http.GetAsync($"/KnowledgePoints/Details/{kpId}");
        kpDetailsResponse.EnsureSuccessStatusCode();
        var kpDetailsHtml = await kpDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, kpDetailsHtml, "Knowledge Point details should show associated category");

        // 5. Verify Association in Category Details
        var catDetailsResponse = await _http.GetAsync($"/KnowledgeCategories/Details/{categoryId}");
        catDetailsResponse.EnsureSuccessStatusCode();
        var catDetailsHtml = await catDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(kpTitle, catDetailsHtml, "Category details should show associated knowledge point");
    }

    [TestMethod]
    public async Task TestKnowledgePointCategoryAssociationAtCreation()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = "Created Category";
        var createCatToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "TotalCategoryId", _courseId.ToString() },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/KnowledgeCategories/Create", createCatContent);
        Assert.AreEqual(HttpStatusCode.Found, createCatResponse.StatusCode);
        var categoryId = createCatResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create a Knowledge Point with the category
        var kpTitle = "KP with Category";
        var kpContent = "KP Content";
        var createKpToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createKpContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", kpTitle },
            { "Content", kpContent },
            { "SelectedKnowledgeCategoryIds[0]", categoryId },
            { "__RequestVerificationToken", createKpToken }
        });
        var createKpResponse = await _http.PostAsync("/KnowledgePoints/Create", createKpContent);
        Assert.AreEqual(HttpStatusCode.Found, createKpResponse.StatusCode);
        var kpId = createKpResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Verify Association in Knowledge Point Details
        var kpDetailsResponse = await _http.GetAsync($"/KnowledgePoints/Details/{kpId}");
        kpDetailsResponse.EnsureSuccessStatusCode();
        var kpDetailsHtml = await kpDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, kpDetailsHtml, "Knowledge Point details should show associated category after creation");

        // 4. Verify Association in Category Details
        var catDetailsResponse = await _http.GetAsync($"/KnowledgeCategories/Details/{categoryId}");
        catDetailsResponse.EnsureSuccessStatusCode();
        var catDetailsHtml = await catDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(kpTitle, catDetailsHtml, "Category details should show associated knowledge point after creation");
    }

    [TestMethod]
    public async Task KnowledgeCategoryKnowledgePointOrderAffectsMiniProgramApi()
    {
        await LoginAsAdminAsync();

        var categoryToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var categoryResponse = await _http.PostAsync("/KnowledgeCategories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", $"Ordered category {Guid.NewGuid()}" },
            { "IsFree", "true" },
            { "TotalCategoryId", _courseId.ToString() },
            { "__RequestVerificationToken", categoryToken }
        }));
        Assert.AreEqual(HttpStatusCode.Found, categoryResponse.StatusCode);
        var categoryId = categoryResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        async Task<(string Id, string Title)> CreateKnowledgePointAsync(string title)
        {
            var token = await GetAntiCsrfToken("/KnowledgePoints/Create");
            var response = await _http.PostAsync("/KnowledgePoints/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "Title", title },
                { "Content", $"Content for {title}" },
                { "SelectedKnowledgeCategoryIds[0]", categoryId },
                { "__RequestVerificationToken", token }
            }));
            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            return (response.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0], title);
        }

        var first = await CreateKnowledgePointAsync($"First knowledge point {Guid.NewGuid()}");
        var second = await CreateKnowledgePointAsync($"Second knowledge point {Guid.NewGuid()}");

        var detailsUrl = $"/KnowledgeCategories/Details/{categoryId}";
        var orderToken = await GetAntiCsrfToken(detailsUrl);
        var detailsHtml = await (await _http.GetAsync(detailsUrl)).Content.ReadAsStringAsync();
        Assert.Contains("knowledge-points-sortable", detailsHtml);

        var orderRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/KnowledgeCategories/UpdateKnowledgePointOrder/{categoryId}")
        {
            Content = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(new[] { second.Id, first.Id }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        orderRequest.Headers.Add("RequestVerificationToken", orderToken);
        var orderResponse = await _http.SendAsync(orderRequest);
        Assert.AreEqual(HttpStatusCode.OK, orderResponse.StatusCode);

        var apiResponse = await _http.GetAsync($"/api/KnowledgePoints?knowledgeCategoryId={categoryId}");
        apiResponse.EnsureSuccessStatusCode();
        var apiJson = await apiResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(
            apiJson.IndexOf(second.Title, StringComparison.Ordinal) < apiJson.IndexOf(first.Title, StringComparison.Ordinal),
            "The mini-program API should return knowledge points in the saved category order.");

        var invalidRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/KnowledgeCategories/UpdateKnowledgePointOrder/{categoryId}")
        {
            Content = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(new[] { first.Id }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        invalidRequest.Headers.Add("RequestVerificationToken", orderToken);
        var invalidResponse = await _http.SendAsync(invalidRequest);
        Assert.AreEqual(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }
    [TestMethod]
    [DataRow(" 伯牙", " 伯牙", "伯牙")]
    [DataRow("\t伯牙 \r\n", "\t伯牙 \r\n", "伯牙")]
    [DataRow("\u3000伯牙\u00a0", "\u3000伯牙\u00a0", "伯牙")]
    [DataRow("Ludwig van Beethoven", "Ludwig van Beethoven", "Ludwig van Beethoven")]
    public async Task KnowledgePointAnswersSupportClientsThatTrimSelectedOptions(
        string storedAnswer, string selectedOption, string expectedAnswer)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new KnowledgeCategory { Title = "Answer comparison course", TotalCategoryId = _courseId, IsFree = true };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Related lesson", Content = "Lesson content" };
        var metadata = Newtonsoft.Json.JsonConvert.SerializeObject(new { options = new[] { selectedOption, "Another musician" } });
        var question = new Question
        {
            QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(isFree: true),
            Id = Guid.NewGuid(), Content = "Who played the instrument?", QuestionType = QuestionType.Choice,
            StandardAnswer = storedAnswer, Metadata = metadata
        };
        db.AddRange(category, point, question,
            new KnowledgeCategoryKnowledgePoint { KnowledgeCategoryId = category.Id, KnowledgePointId = point.Id },
            new KnowledgePointQuestion { KnowledgePointId = point.Id, QuestionId = question.Id });
        await db.SaveChangesAsync();

        var response = await _http.GetAsync($"/api/KnowledgePoints?knowledgeCategoryId={category.Id}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var linkedQuestion = json.RootElement.EnumerateArray().Single()
            .GetProperty("questions").EnumerateArray().Single();
        Assert.AreEqual(question.Id, linkedQuestion.GetProperty("id").GetGuid());
        var answer = linkedQuestion.GetProperty("answer").GetString();
        Assert.AreEqual(expectedAnswer, answer);
        // Deployed clients trim the selected option before checking the API answer.
        Assert.AreEqual(selectedOption.Trim(), answer);

        using var verificationScope = _server.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var stored = await verificationDb.Questions.FindAsync(question.Id);
        Assert.IsNotNull(stored);
        Assert.AreEqual(storedAnswer, stored.StandardAnswer);
        Assert.AreEqual(metadata, stored.Metadata);
    }

    [TestMethod]
    public async Task ContentChecksRequirePermissionAndRenderReadOnlyResults()
    {
        var anonymous = await _http.GetAsync("/ContentChecks/Results");
        Assert.AreEqual(HttpStatusCode.Found, anonymous.StatusCode);
        await LoginAsAdminAsync();
        var initial = await _http.GetStringAsync("/ContentChecks");
        StringAssert.Contains(initial, "Start check");
        Assert.IsFalse(initial.Contains("name=\"search\""));
        using (var scope = _server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            for (var i = 0; i < 55; i++)
                db.Tags.Add(new Tag { DisplayName = $"Diagnostic tag {i}", NormalizedName = $"DIAGNOSTIC TAG {i}", IsFree = false });
            await db.SaveChangesAsync();
        }
        var first = await _http.GetStringAsync("/ContentChecks/Results?rule=TAG001&search=Diagnostic");
        Assert.AreEqual(12, Regex.Matches(first, "<article ").Count);
        StringAssert.Contains(first, "Page 1 of 5");
        var second = await _http.GetStringAsync("/ContentChecks/Results?rule=TAG001&search=Diagnostic&page=2");
        Assert.AreEqual(12, Regex.Matches(second, "<article ").Count);
        var last = await _http.GetStringAsync("/ContentChecks/Results?rule=TAG001&search=Diagnostic&page=5");
        Assert.AreEqual(7, Regex.Matches(last, "<article ").Count);
        var filter = await _http.GetStringAsync("/ContentChecks/Results?rule=TAG001&search=Diagnostic%20tag%2054");
        Assert.AreEqual(1, Regex.Matches(filter, "<article ").Count);
        Assert.IsFalse(filter.Contains("<table"));
        var detailsUrl = WebUtility.HtmlDecode(Regex.Match(filter, "href=\"([^\"]*/ContentChecks/Details[^\"]*)\"").Groups[1].Value);
        Assert.IsFalse(string.IsNullOrEmpty(detailsUrl));
        var detail = await _http.GetStringAsync(detailsUrl);
        StringAssert.Contains(detail, "/Tags/Edit/");
        StringAssert.Contains(detail, "What students experience");
        using var checkScope = _server!.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(55, checkDb.Tags.Count(t => t.NormalizedName.StartsWith("DIAGNOSTIC") && !t.IsFree));
        var cultureResponse = await _http.GetAsync("/Culture/Set?culture=zh-CN&returnUrl=/ContentChecks");
        Assert.AreEqual(HttpStatusCode.Found, cultureResponse.StatusCode);
        var chinese = WebUtility.HtmlDecode(await _http.GetStringAsync(detailsUrl));
        var culture = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
        var viewResources = new System.Resources.ResourceManager("Aiursoft.WeChatExam.Resources.Views.ContentChecks.Details", typeof(Startup).Assembly);
        var controllerResources = new System.Resources.ResourceManager("Aiursoft.WeChatExam.Resources.Controllers.Management.ContentChecksController", typeof(Startup).Assembly);
        StringAssert.Contains(chinese, viewResources.GetResourceSet(culture, true, false)!.GetString("What students experience")!);
        StringAssert.Contains(chinese, controllerResources.GetResourceSet(culture, true, false)!.GetString("No VIP can unlock this tag")!);
        // Correcting a shared tag between listing and opening it returns a fresh list.
        var tag = checkDb.Tags.Single(t => t.DisplayName == "Diagnostic tag 54");
        tag.IsFree = true;
        await checkDb.SaveChangesAsync();
        var stale = await _http.GetAsync(detailsUrl);
        Assert.AreEqual(HttpStatusCode.Found, stale.StatusCode);
        StringAssert.Contains(stale.Headers.Location!.OriginalString, "changed=true");
        var stalePage = await _http.GetAsync(stale.Headers.Location);
        Assert.AreEqual(HttpStatusCode.OK, stalePage.StatusCode);
    }

    [TestMethod]
    public async Task ContentCheckPermissionDoesNotExposeUnauthorizedEditLinks()
    {
        using var scope = _server!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Restricted editor" };
        var tag = new Tag { DisplayName = "Diagnostic shared tag", NormalizedName = "DIAGNOSTIC SHARED TAG", IsFree = false };
        db.AddRange(question, tag);
        await db.SaveChangesAsync();
        db.QuestionTags.Add(new QuestionTag { QuestionId = question.Id, TagId = tag.Id });
        await db.SaveChangesAsync();
        foreach (var permitted in new[] { false, true })
        {
            var email = $"content-check-{permitted}@example.com";
            var user = new User { UserName = email, Email = email, DisplayName = "Reviewer", EmailConfirmed = true };
            Assert.IsTrue((await manager.CreateAsync(user, "Admin@123456!")).Succeeded);
            if (permitted)
                Assert.IsTrue((await manager.AddClaimAsync(user, new System.Security.Claims.Claim(
                    Authorization.AppPermissions.Type, Authorization.AppPermissionNames.CanManageTags))).Succeeded);
            var client = _http;
            var login = await client.GetStringAsync("/Account/Login");
            var token = Regex.Match(login, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
            var signedIn = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["EmailOrUserName"] = email, ["Password"] = "Admin@123456!", ["__RequestVerificationToken"] = token
            }));
            Assert.AreEqual(HttpStatusCode.Found, signedIn.StatusCode);
            var response = await client.GetAsync("/ContentChecks/Results?rule=TAG001&search=Diagnostic%20shared%20tag");
            if (permitted)
            {
                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                var cards = await response.Content.ReadAsStringAsync();
                var detailUrl = WebUtility.HtmlDecode(Regex.Match(cards, "href=\"([^\"]*/ContentChecks/Details[^\"]*)\"").Groups[1].Value);
                Assert.IsFalse(string.IsNullOrEmpty(detailUrl));
                var html = await client.GetStringAsync(detailUrl);
                Assert.IsTrue(html.Contains($"/Tags/Edit/{tag.Id}"));
                StringAssert.Contains(html, "Restricted editor");
                Assert.Contains("No knowledge point is linked to this exercise.", html);
                Assert.DoesNotContain("Linked exercise", html);
                Assert.IsFalse(html.Contains($"/Questions/Edit/{question.Id}"));
            }
            else
            {
                Assert.IsTrue(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Found);
                Assert.IsFalse((await response.Content.ReadAsStringAsync()).Contains("Diagnostic shared tag"));
            }
        }
    }

    [TestMethod]
    public async Task GuidedCardsGroupSharedExercisesAndKeepProgramReviewSeparate()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var subject = new Category { Id = Guid.NewGuid(), Title = "Grouped course", ParentId = null, IsFree = true };
        var tag = new Tag { DisplayName = "Grouped shared tag", NormalizedName = "GROUPED SHARED TAG", IsFree = false };
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Grouped lesson", Content = "Public lesson" };
        db.AddRange(subject, tag, lesson, new VipProduct { Name = "Course VIP", TotalCategory = new TotalCategory { Id = subject.Id, Title = subject.Title }, DurationDays = 365 });
        await db.SaveChangesAsync();
        for (var i = 0; i < 50; i++)
        {
            var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), CategoryId = subject.Id, Content = $"Grouped exercise {i}" };
            db.AddRange(question, new QuestionTag { QuestionId = question.Id, TagId = tag.Id },
                new KnowledgePointQuestion { KnowledgePointId = lesson.Id, QuestionId = question.Id });
        }
        await db.SaveChangesAsync();
        var results = await _http.GetStringAsync("/ContentChecks/Results?rule=KP001&search=Grouped%20lesson");
        Assert.AreEqual(1, Regex.Matches(results, "<article ").Count);
        var card = Regex.Match(results, "<article[\\s\\S]*?</article>").Value;
        StringAssert.Contains(card, "Pricing arrangement");
        Assert.IsFalse(card.Contains("text-danger"));
        Assert.IsFalse(card.Contains("<table"));
        var link = WebUtility.HtmlDecode(Regex.Match(card, "href=\"([^\"]*/ContentChecks/Details[^\"]*)\"").Groups[1].Value);
        var detail = await _http.GetStringAsync(link);
        for (var i = 0; i < 50; i++) Assert.IsTrue(detail.Contains($"Grouped exercise {i}"));
        StringAssert.Contains(detail, "Shared tags affect 50 exercises in 1 courses.");
        StringAssert.Contains(detail, "rule=KP001");
        var seen = new HashSet<string>();
        var current = detail;
        while (true)
        {
            Assert.IsTrue(seen.Add(Regex.Match(current, "data-exercise-id=\"([^\"]+)\"").Groups[1].Value));
            var next = Regex.Match(current, "href=\"([^\"]+)\" class=\"btn btn-outline-primary\">Next exercise</a>");
            if (!next.Success) break;
            current = await _http.GetStringAsync(WebUtility.HtmlDecode(next.Groups[1].Value));
        }
        Assert.AreEqual(50, seen.Count, "Every exercise must be reachable without duplicate or invented relationships.");

        var team = await _http.GetStringAsync("/ContentChecks/Results?view=engineering&search=Grouped%20lesson");
        StringAssert.Contains(team, "These items need developer review");
        Assert.AreEqual(1, Regex.Matches(team, "<article ").Count);
    }

    [TestMethod]
    public async Task RelationshipCardsKeepExactEdgesAndShowCourseSpecificAccess()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new Category { Id = Guid.NewGuid(), Title = "Relation course", ParentId = null };
        var other = new Category { Id = Guid.NewGuid(), Title = "Other relation course", ParentId = null };
        var taxonomy = new Taxonomy { Name = "Relation taxonomy" };
        var tag = new Tag { DisplayName = "Relation paid tag", NormalizedName = "RELATION PAID TAG", IsFree = false, Taxonomy = taxonomy };
        var first = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Relation first lesson", Content = "Public" };
        var second = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Relation second lesson", Content = "Public" };
        var a = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Relation first exercise " + new string('x', 220), CategoryId = course.Id };
        var b = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Relation second exercise", CategoryId = course.Id };
        var free = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Relation free exercise", CategoryId = course.Id };
        var product = new VipProduct { Name = "Relation VIP", TotalCategory = new TotalCategory { Id = course.Id, Title = course.Title }, DurationDays = 365, IsEnabled = false };
        db.AddRange(course, other, tag, first, second, a, b, free, product,
            new QuestionTag { QuestionId = a.Id, TagId = tag.Id, Tag = tag }, new QuestionTag { QuestionId = b.Id, TagId = tag.Id, Tag = tag },
            new KnowledgePointQuestion { KnowledgePointId = first.Id, QuestionId = a.Id },
            new KnowledgePointQuestion { KnowledgePointId = second.Id, QuestionId = b.Id },
            new KnowledgeCategoryKnowledgePoint { KnowledgePointId = first.Id, KnowledgeCategoryId = other.Id, KnowledgeCategory = new KnowledgeCategory { Id = other.Id, Title = other.Title, TotalCategoryId = other.Id, TotalCategory = new TotalCategory { Id = other.Id, Title = other.Title } } });
        await db.SaveChangesAsync();
        var results = await _http.GetStringAsync("/ContentChecks/Results?rule=KP001&search=Relation");
        Assert.AreEqual(1, Regex.Matches(results, "<article ").Count);
        Assert.Contains("Review how public content and paid content are connected", results);
        var detailUrl = WebUtility.HtmlDecode(Regex.Match(results, "href=\"([^\"]*/ContentChecks/Details[^\"]*)\"").Groups[1].Value);
        foreach (var (question, linked, unrelated) in new[] { (a, first, second), (b, second, first) })
        {
            var detail = await _http.GetStringAsync(detailUrl + "&exerciseId=" + question.Id);
            var relation = Regex.Match(detail, "class=\"check-relationship\"[\\s\\S]*?</section>").Value;
            Assert.Contains($"data-exercise-id=\"{question.Id}\"", relation);
            Assert.Contains($"/KnowledgePoints/Edit/{linked.Id}", relation);
            Assert.DoesNotContain($"/KnowledgePoints/Edit/{unrelated.Id}", relation);
            Assert.Contains($"/Questions/Edit/{question.Id}", relation);
            Assert.Contains($"/TotalCategories/Edit/{course.Id}", relation);
            Assert.Contains($"/Tags/Edit/{tag.Id}", detail);
            Assert.Contains($"/Taxonomies/Edit/{taxonomy.Id}", detail);
            Assert.Contains($"/VipProducts/Edit/{product.Id}", detail);
            Assert.Contains("Sales disabled", detail);
            Assert.Contains("data-allowed=\"false\"", detail);
            Assert.Contains("data-allowed=\"true\"", detail);
            if (question == a) Assert.Contains("This account needs the exercise's own course VIP.", WebUtility.HtmlDecode(detail));
            Assert.Contains("checkReturnUrl=", relation);
            if (question == a) Assert.Contains("Read full exercise", relation);
        }
        // Sale availability is separate from an already purchased, active membership.
        var report = await scope.ServiceProvider.GetRequiredService<Services.ContentCheckService>().ScanAsync([], false);
        Assert.IsTrue(report.Exercises[a.Id].Access.Single(x => x.Scenario == "Subject VIP: Relation course").Decision.Allowed);
        Assert.IsFalse(report.Exercises[a.Id].Access.Single(x => x.Scenario == "Subject VIP: Other relation course").Decision.Allowed);
        Assert.AreEqual(Models.ContentChecksViewModels.ContentPricing.Public, report.Exercises[free.Id].Question.Pricing);
        Assert.IsTrue(report.Exercises[free.Id].Access.First().Decision.Allowed);
    }

    [TestMethod]
    [DataRow("KnowledgePoints")]
    [DataRow("Questions")]
    [DataRow("Tags")]
    [DataRow("Taxonomies")]
    [DataRow("KnowledgeCategories")]
    [DataRow("TotalCategories")]
    [DataRow("VipProducts")]
    [DataRow("Papers")]
    public async Task EditorsReturnToCheckupAfterValidationAndSaveWithoutExternalRedirects(string controller)
    {
        await LoginAsAdminAsync();
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = new Category { Id = Guid.NewGuid(), Title = "Origin course", ParentId = null };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Origin lesson", Content = "Public" };
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Origin exercise", CategoryId = category.Id };
        var tag = new Tag { DisplayName = "Origin tag", NormalizedName = "ORIGIN TAG", IsFree = false };
        var taxonomy = new Taxonomy { Name = "Origin taxonomy" };
        var product = new VipProduct { Name = "Origin VIP", TotalCategory = new TotalCategory { Id = category.Id, Title = category.Title }, DurationDays = 365 };
        var paper = new Paper { Id = Guid.NewGuid(), Title = "Origin paper", TimeLimit = 180 };
        var directory = new KnowledgeCategory { Title = "Origin directory", TotalCategoryId = category.Id };
        db.AddRange(category, directory, point, question, tag, taxonomy, product, paper);
        await db.SaveChangesAsync();
        var id = controller switch
        {
            "KnowledgePoints" => point.Id.ToString(), "Questions" => question.Id.ToString(), "Tags" => tag.Id.ToString(),
            "Taxonomies" => taxonomy.Id.ToString(), "KnowledgeCategories" => directory.Id.ToString(), "TotalCategories" => category.Id.ToString(),
            "VipProducts" => product.Id.ToString(), _ => paper.Id.ToString()
        };
        const string origin = "/ContentChecks/Results?view=information&search=Origin&page=2";
        var path = $"/{controller}/Edit/{id}";
        var edit = await _http.GetStringAsync(path + "?checkReturnUrl=" + Uri.EscapeDataString(origin));
        Assert.Contains("Save and return to content checks", edit);
        Assert.Contains(WebUtility.HtmlEncode(origin), edit);
        var token = Regex.Match(edit, @"name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)""").Groups[1].Value;
        var values = new Dictionary<string, string>
        {
            ["Id"] = id, ["checkReturnUrl"] = origin, ["__RequestVerificationToken"] = token,
            ["Title"] = "", ["Content"] = "", ["DisplayName"] = "", ["Name"] = ""
        };
        var invalid = await _http.PostAsync(path, new FormUrlEncodedContent(values));
        Assert.AreEqual(HttpStatusCode.OK, invalid.StatusCode);
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Save and return to content checks", invalidHtml);
        Assert.Contains(WebUtility.HtmlEncode(origin), invalidHtml);
        values["Title"] = "Origin updated";
        values["Content"] = "Origin updated";
        values["DisplayName"] = "Origin updated";
        values["Name"] = "Origin updated";
        values["CategoryId"] = category.Id.ToString();
        values["TotalCategoryId"] = category.Id.ToString();
        values["QuestionCategoryId"] = periodId.ToString();
        values["QuestionType"] = "0";
        values["GradingStrategy"] = "0";
        values["Options[0]"] = "Correct";
        values["Options[1]"] = "Wrong";
        values["StandardAnswer"] = "Correct";
        values["PriceInFen"] = "100";
        values["DurationDays"] = "365";
        values["TimeLimit"] = "180";
        var saved = await _http.PostAsync(path, new FormUrlEncodedContent(values));
        Assert.AreEqual(HttpStatusCode.Found, saved.StatusCode);
        Assert.AreEqual(origin, saved.Headers.Location!.OriginalString);
        Assert.AreEqual(HttpStatusCode.OK, (await _http.GetAsync(saved.Headers.Location)).StatusCode);
        // Neither a GET nor a forged successful POST can turn the origin into an open redirect.
        foreach (var external in new[] { "https://example.com/", "//example.com/", "/\\example.com/" })
        {
            var unsafeEdit = await _http.GetStringAsync(path + "?checkReturnUrl=" + Uri.EscapeDataString(external));
            Assert.DoesNotContain("name=\"checkReturnUrl\"", unsafeEdit);
            values["checkReturnUrl"] = external;
            var response = await _http.PostAsync(path, new FormUrlEncodedContent(values));
            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            Assert.IsTrue(response.Headers.Location!.OriginalString.StartsWith('/') && !response.Headers.Location.OriginalString.StartsWith("//"));
        }
    }

}
