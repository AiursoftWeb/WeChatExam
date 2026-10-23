using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class KnowledgePointAssociationTests
{
    private readonly HttpClient _http;
    private IHost? _server;

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
        using var reservation = new TestPortReservation();
        _http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        _server = await AppAsync<Startup>(["--applicationName", "Aiursoft.WeChatExam"], port: reservation.Port);
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
    public async Task TestKnowledgePointCategoryAssociation()
    {
        await LoginAsAdminAsync();

        // 1. Create a Category
        var categoryTitle = "Test Association Category";
        var createCatToken = await GetAntiCsrfToken("/Categories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/Categories/Create", createCatContent);
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
            { "SelectedCategoryIds[0]", categoryId },
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
        var catDetailsResponse = await _http.GetAsync($"/Categories/Details/{categoryId}");
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
        var createCatToken = await GetAntiCsrfToken("/Categories/Create");
        var createCatContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createCatToken }
        });
        var createCatResponse = await _http.PostAsync("/Categories/Create", createCatContent);
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
            { "SelectedCategoryIds[0]", categoryId },
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
        var catDetailsResponse = await _http.GetAsync($"/Categories/Details/{categoryId}");
        catDetailsResponse.EnsureSuccessStatusCode();
        var catDetailsHtml = await catDetailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(kpTitle, catDetailsHtml, "Category details should show associated knowledge point after creation");
    }

    [TestMethod]
    public async Task CategoryKnowledgePointOrderAffectsMiniProgramApi()
    {
        await LoginAsAdminAsync();

        var categoryToken = await GetAntiCsrfToken("/Categories/Create");
        var categoryResponse = await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", $"Ordered category {Guid.NewGuid()}" },
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
                { "SelectedCategoryIds[0]", categoryId },
                { "__RequestVerificationToken", token }
            }));
            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            return (response.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0], title);
        }

        var first = await CreateKnowledgePointAsync($"First knowledge point {Guid.NewGuid()}");
        var second = await CreateKnowledgePointAsync($"Second knowledge point {Guid.NewGuid()}");

        var detailsUrl = $"/Categories/Details/{categoryId}";
        var orderToken = await GetAntiCsrfToken(detailsUrl);
        var detailsHtml = await (await _http.GetAsync(detailsUrl)).Content.ReadAsStringAsync();
        Assert.Contains("knowledge-points-sortable", detailsHtml);

        var orderRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Categories/UpdateKnowledgePointOrder/{categoryId}")
        {
            Content = new StringContent(
                Newtonsoft.Json.JsonConvert.SerializeObject(new[] { second.Id, first.Id }),
                System.Text.Encoding.UTF8,
                "application/json")
        };
        orderRequest.Headers.Add("RequestVerificationToken", orderToken);
        var orderResponse = await _http.SendAsync(orderRequest);
        Assert.AreEqual(HttpStatusCode.OK, orderResponse.StatusCode);

        var apiResponse = await _http.GetAsync($"/api/KnowledgePoints?categoryId={categoryId}");
        apiResponse.EnsureSuccessStatusCode();
        var apiJson = await apiResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(
            apiJson.IndexOf(second.Title, StringComparison.Ordinal) < apiJson.IndexOf(first.Title, StringComparison.Ordinal),
            "The mini-program API should return knowledge points in the saved category order.");

        var invalidRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Categories/UpdateKnowledgePointOrder/{categoryId}")
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
        var question = new Question { Id = Guid.NewGuid(), Content = "Restricted editor" };
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
        var lesson = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Grouped lesson", ParentId = null, Content = "Public lesson" };
        db.AddRange(subject, tag, lesson, new VipProduct { Name = "Course VIP", CategoryId = subject.Id, DurationDays = 365 });
        await db.SaveChangesAsync();
        for (var i = 0; i < 50; i++)
        {
            var question = new Question { Id = Guid.NewGuid(), CategoryId = subject.Id, Content = $"Grouped exercise {i}" };
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
        var team = await _http.GetStringAsync("/ContentChecks/Results?view=engineering&search=Grouped%20lesson");
        StringAssert.Contains(team, "These items need developer review");
        Assert.AreEqual(1, Regex.Matches(team, "<article ").Count);
    }

}
