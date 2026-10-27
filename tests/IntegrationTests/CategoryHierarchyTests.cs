using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CategoryHierarchyTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly Guid _courseId = Guid.NewGuid();

    public CategoryHierarchyTests()
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
        _server = await AppAsync<Startup>([], port: reservation.Port);
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
    public async Task TestSubCategoryAsParent()
    {
        await LoginAsAdminAsync();

        // 1. Create Root Category
        var rootTitle = "Root Category";
        var createToken1 = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent1 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", _courseId.ToString() },
            { "Title", rootTitle },
            { "__RequestVerificationToken", createToken1 }
        });
        var createResponse1 = await _http.PostAsync("/KnowledgeCategories/Create", createContent1);
        Assert.AreEqual(HttpStatusCode.Found, createResponse1.StatusCode);
        var rootId = createResponse1.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Create Level 1 Category (Child of Root)
        var level1Title = "Level 1 Category";
        var createToken2 = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent2 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", _courseId.ToString() },
            { "Title", level1Title },
            { "ParentKnowledgeCategoryId", rootId },
            { "__RequestVerificationToken", createToken2 }
        });
        var createResponse2 = await _http.PostAsync("/KnowledgeCategories/Create", createContent2);
        Assert.AreEqual(HttpStatusCode.Found, createResponse2.StatusCode);
        var level1Id = createResponse2.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 3. Check if Level 1 Category is available as parent in Create page
        var createPageResponse = await _http.GetAsync("/KnowledgeCategories/Create");
        createPageResponse.EnsureSuccessStatusCode();
        var createPageHtml = await createPageResponse.Content.ReadAsStringAsync();
        
        // Before fix, Level 1 Category would NOT be in AvailableParents because it's not a root category.
        // Now it SHOULD be there.
        Assert.IsTrue(createPageHtml.Contains(level1Id), "Level 1 category should be available as a parent in Create page");
        Assert.IsTrue(createPageHtml.Contains(level1Title), "Level 1 category title should be visible in Create page");

        // 4. Create Level 2 Category (Child of Level 1)
        var level2Title = "Level 2 Category";
        var createToken3 = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent3 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", _courseId.ToString() },
            { "Title", level2Title },
            { "ParentKnowledgeCategoryId", level1Id },
            { "__RequestVerificationToken", createToken3 }
        });
        var createResponse3 = await _http.PostAsync("/KnowledgeCategories/Create", createContent3);
        Assert.AreEqual(HttpStatusCode.Found, createResponse3.StatusCode);
        var level2Id = createResponse3.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 5. Verify Hierarchy in details
        var detailsResponse = await _http.GetAsync($"/KnowledgeCategories/Details/{level2Id}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(detailsHtml.Contains(level2Title));
        Assert.IsTrue(detailsHtml.Contains(level1Title)); // Should show its parent title
    }

    [TestMethod]
    public async Task TestCategoryOrdering()
    {
        await LoginAsAdminAsync();

        // 1. Create two root categories
        var cat1Title = "Cat 1";
        var createToken1 = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent1 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", _courseId.ToString() },
            { "Title", cat1Title },
            { "__RequestVerificationToken", createToken1 }
        });
        var createResponse1 = await _http.PostAsync("/KnowledgeCategories/Create", createContent1);
        var cat1Id = createResponse1.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        var cat2Title = "Cat 2";
        var createToken2 = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent2 = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", _courseId.ToString() },
            { "Title", cat2Title },
            { "__RequestVerificationToken", createToken2 }
        });
        var createResponse2 = await _http.PostAsync("/KnowledgeCategories/Create", createContent2);
        var cat2Id = createResponse2.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        // 2. Change order via API
        var orderToken = await GetAntiCsrfToken("/KnowledgeCategories");
        var orderContent = new StringContent(
            Newtonsoft.Json.JsonConvert.SerializeObject(new[] { Guid.Parse(cat2Id), Guid.Parse(cat1Id) }),
            System.Text.Encoding.UTF8,
            "application/json");
        
        // Add header for anti-CSRF
        var request = new HttpRequestMessage(HttpMethod.Post, $"/KnowledgeCategories/UpdateOrder?totalCategoryId={_courseId}")
        {
            Content = orderContent
        };
        request.Headers.Add("RequestVerificationToken", orderToken);
        
        var orderResponse = await _http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, orderResponse.StatusCode);

        // 3. Verify the same ordering in the knowledge directory list.
        var apiResponse = await _http.GetAsync($"/KnowledgeCategories?totalCategoryId={_courseId}");
        apiResponse.EnsureSuccessStatusCode();
        var apiJson = await apiResponse.Content.ReadAsStringAsync();
        
        // cat2 should come before cat1
        var cat1Index = apiJson.IndexOf(cat1Title, StringComparison.Ordinal);
        var cat2Index = apiJson.IndexOf(cat2Title, StringComparison.Ordinal);
        
        Assert.IsTrue(cat2Index < cat1Index, "Cat 2 should appear before Cat 1 in the API response after reordering");
    }

    [TestMethod]
    public async Task KnowledgeDirectoriesRejectCrossCourseParentsCyclesAndDeletionWithContent()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var otherCourse = new TotalCategory { Title = "Other course" };
        var root = new KnowledgeCategory { Title = "Early music", TotalCategoryId = _courseId };
        var child = new KnowledgeCategory { Title = "Overview", TotalCategoryId = _courseId, ParentKnowledgeCategoryId = root.Id };
        var other = new KnowledgeCategory { Title = "Overview", TotalCategoryId = otherCourse.Id };
        var lesson = new KnowledgePoint { Title = "Retained lesson", Content = "Retained content" };
        db.AddRange(otherCourse, root, child, other, lesson,
            new KnowledgeCategoryKnowledgePoint { KnowledgeCategoryId = child.Id, KnowledgePointId = lesson.Id });
        await db.SaveChangesAsync();

        var creation = await _http.PostAsync("/KnowledgeCategories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Invalid directory", ["TotalCategoryId"] = _courseId.ToString(),
            ["ParentKnowledgeCategoryId"] = other.Id.ToString(),
            ["__RequestVerificationToken"] = await GetAntiCsrfToken("/KnowledgeCategories/Create")
        }));
        Assert.AreEqual(HttpStatusCode.OK, creation.StatusCode);
        Assert.Contains("The parent must belong to the same total category.", await creation.Content.ReadAsStringAsync());
        var path = $"/KnowledgeCategories/Edit/{root.Id}";
        var values = new Dictionary<string, string>
        {
            ["Id"] = root.Id.ToString(), ["Title"] = root.Title, ["TotalCategoryId"] = otherCourse.Id.ToString(),
            ["ParentKnowledgeCategoryId"] = child.Id.ToString(), ["__RequestVerificationToken"] = await GetAntiCsrfToken(path)
        };
        var cycle = await _http.PostAsync(path, new FormUrlEncodedContent(values));
        Assert.AreEqual(HttpStatusCode.OK, cycle.StatusCode);
        Assert.Contains("Cannot create circular reference", await cycle.Content.ReadAsStringAsync());
        values["ParentKnowledgeCategoryId"] = "";
        var edited = await _http.PostAsync(path, new FormUrlEncodedContent(values));
        Assert.AreEqual(HttpStatusCode.Found, edited.StatusCode);
        db.ChangeTracker.Clear();
        Assert.AreEqual(_courseId, (await db.KnowledgeCategories.FindAsync(root.Id))!.TotalCategoryId);
        foreach (var id in new[] { root.Id, child.Id })
        {
            var deletionPath = $"/KnowledgeCategories/Delete/{id}";
            var response = await _http.PostAsync(deletionPath, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = await GetAntiCsrfToken("/KnowledgeCategories/Create")
            }));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(await db.KnowledgeCategories.FindAsync(id));
        }
        var editor = WebUtility.HtmlDecode(await _http.GetStringAsync("/KnowledgePoints/Create"));
        Assert.Contains("Test course / Early music / Overview", editor);
        Assert.Contains("Other course / Overview", editor);
        Assert.IsNotNull(await db.KnowledgePoints.FindAsync(lesson.Id));
    }
}
