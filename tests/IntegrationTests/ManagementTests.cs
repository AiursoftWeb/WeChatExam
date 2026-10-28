using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ManagementTests
{
    private readonly HttpClient _http;
    private IHost? _server;
    private readonly List<Guid> _scheduledExamIds = [];

    public ManagementTests()
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
        // These cases create actual answer records; remove only their own schedules
        // before the shared in-memory store is used by other integration classes.
        using (var scope = _server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var records = await db.ExamRecords.Include(r => r.AnswerRecords)
                .Where(r => _scheduledExamIds.Contains(r.ExamId)).ToListAsync();
            db.AnswerRecords.RemoveRange(records.SelectMany(r => r.AnswerRecords));
            db.ExamRecords.RemoveRange(records);
            db.Exams.RemoveRange(await db.Exams.Where(e => _scheduledExamIds.Contains(e.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }
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
        var email = "admin@default.com"; // Default admin email from SeedAsync
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
        Assert.AreEqual("/Dashboard/Index", loginResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task CategoriesCrudTest()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Directory course" };
        db.Add(course);
        await db.SaveChangesAsync();

        // 1. Create Category
        var categoryTitle = $"Test-Category-{Guid.NewGuid()}";
        var createToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", course.Id.ToString() },
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createToken }
        });
        
        var createResponse = await _http.PostAsync("/KnowledgeCategories/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        Assert.StartsWith("/KnowledgeCategories/Details", detailsUrl);
        
        var categoryId = detailsUrl.Split('/').Last().Split('?')[0]; // Extract ID

        // 2. Read Category (Details)
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, detailsHtml);

        // 3. Edit Category
        var newTitle = $"Updated-Category-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/KnowledgeCategories/Edit/{categoryId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", categoryId },
            { "TotalCategoryId", course.Id.ToString() },
            { "Title", newTitle },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/KnowledgeCategories/Edit/{categoryId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync(detailsUrl);
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newTitle, verifyHtml);

        // 4. Delete Category
        var deleteToken = await GetAntiCsrfToken($"/KnowledgeCategories/Delete/{categoryId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/KnowledgeCategories/Delete/{categoryId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location!.StartsWith("/KnowledgeCategories"));

        // Verify Deletion
        var indexResponse = await _http.GetAsync($"/KnowledgeCategories/Index?totalCategoryId={course.Id}");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newTitle, indexHtml);
    }

    [TestMethod]
    public async Task CategoriesVisualTreeIsRenderedPerRootTest()
    {
        await LoginAsAdminAsync();
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var course = new TotalCategory { Title = "Directory course" };
        db.Add(course);
        await db.SaveChangesAsync();

        var rootTitle = $"Tree-Root-{Guid.NewGuid()}";
        var rootToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var rootResponse = await _http.PostAsync("/KnowledgeCategories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", course.Id.ToString() },
            { "Title", rootTitle },
            { "__RequestVerificationToken", rootToken }
        }));
        Assert.AreEqual(HttpStatusCode.Found, rootResponse.StatusCode);
        var rootId = rootResponse.Headers.Location!.OriginalString.Split('/').Last().Split('?')[0];

        var childTitle = $"Tree-Child-{Guid.NewGuid()}";
        var childToken = await GetAntiCsrfToken("/KnowledgeCategories/Create");
        var childResponse = await _http.PostAsync("/KnowledgeCategories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "TotalCategoryId", course.Id.ToString() },
            { "Title", childTitle },
            { "ParentKnowledgeCategoryId", rootId },
            { "__RequestVerificationToken", childToken }
        }));
        Assert.AreEqual(HttpStatusCode.Found, childResponse.StatusCode);

        var indexResponse = await _http.GetAsync($"/KnowledgeCategories/Index?totalCategoryId={course.Id}");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        var safeRootId = $"C_{rootId.Replace("-", string.Empty)}";

        Assert.Contains("id=\"category-tree-roots\"", indexHtml);
        Assert.Contains($"data-source-id=\"mermaid-source-{safeRootId}\"", indexHtml);
        Assert.Contains($"id=\"mermaid-source-{safeRootId}\"", indexHtml);
        Assert.Contains(rootTitle, indexHtml);
        Assert.Contains(childTitle, indexHtml);
        Assert.Contains("/js/knowledge-categories.js", indexHtml);
        Assert.Contains("category-diagram-node", indexHtml);
        Assert.DoesNotContain("style='width:180px;padding:8px;text-align:left'", indexHtml);
    }

    [TestMethod]
    public async Task KnowledgePointsCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create KnowledgePoint
        var title = $"Test-KP-{Guid.NewGuid()}";
        var content = "This is a test knowledge point content.";
        var createToken = await GetAntiCsrfToken("/KnowledgePoints/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", title },
            { "Content", content },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/KnowledgePoints/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        
        var kpId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read KnowledgePoint
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(title, detailsHtml);
        Assert.Contains(content, detailsHtml);

        // 3. Edit KnowledgePoint
        var newTitle = $"Updated-KP-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/KnowledgePoints/Edit/{kpId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", kpId },
            { "Title", newTitle },
            { "Content", content },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/KnowledgePoints/Edit/{kpId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Delete KnowledgePoint
        var deleteToken = await GetAntiCsrfToken($"/KnowledgePoints/Delete/{kpId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/KnowledgePoints/Delete/{kpId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/KnowledgePoints/Index" || location == "/KnowledgePoints");
    }

    [TestMethod]
    public async Task QuestionsCrudTest()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();

        // 1. Create Question
        var qText = $"Test Question {Guid.NewGuid()}";
        var qType = "0";
        var qStrategy= "0";
        var createToken = await GetAntiCsrfToken($"/Questions/Create?questionCategoryId={periodId}");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", qType },
            { "GradingStrategy", qStrategy },
            { "QuestionCategoryId", periodId.ToString() },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "Explanation", "A is correct" },
            { "__RequestVerificationToken", createToken }
        });
        var createResponse = await _http.PostAsync("/Questions/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        var questionId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read Question
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(qText, detailsHtml);

        // 3. Edit Question
        var newText = $"Updated Question {Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/Questions/Edit/{questionId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", questionId },
            { "questionType", qType },
            { "GradingStrategy", qStrategy },
            { "Content", newText },
            { "QuestionCategoryId", periodId.ToString() },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "B" }, // Changed answer
            { "Explanation", "A is correct" },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Questions/Edit/{questionId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Delete Question
        var deleteToken = await GetAntiCsrfToken($"/Questions/Delete/{questionId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Questions/Delete/{questionId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
    }

    [TestMethod]
    public async Task QuestionNavigationReturnUrlTest()
    {
        var periodId = await QuestionPeriodAssignmentTests.AddPeriod(_server!.Services);
        await LoginAsAdminAsync();

        var qToken = await GetAntiCsrfToken($"/Questions/Create?questionCategoryId={periodId}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", "Navigation Test Question" },
            { "QuestionType", "0" },
            { "GradingStrategy", "0" },
            { "QuestionCategoryId", periodId.ToString() },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "__RequestVerificationToken", qToken }
        }));

        var qIndexUrl = $"/Questions/Index?period={periodId}&page=1";
        var qIndexResponse = await _http.GetAsync(qIndexUrl);
        var qIndexHtml = await qIndexResponse.Content.ReadAsStringAsync();
        var match = Regex.Match(qIndexHtml, @"/Questions/Details/([a-z0-9-]+)");
        Assert.IsTrue(match.Success);
        var questionId = match.Groups[1].Value;

        // 2. Test Edit redirect to ReturnUrl
        var returnUrl = qIndexUrl;
        var editPageUrl = $"/Questions/Edit/{questionId}?returnUrl={Uri.EscapeDataString(returnUrl)}";
        var editToken = await GetAntiCsrfToken(editPageUrl);

        var editContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Id", questionId),
            new KeyValuePair<string, string>("QuestionType", "0"),
            new KeyValuePair<string, string>("GradingStrategy", "0"),
            new KeyValuePair<string, string>("Content", "Updated Content"),
            new KeyValuePair<string, string>("QuestionCategoryId", periodId.ToString()),
            new KeyValuePair<string, string>("Options", "A"),
            new KeyValuePair<string, string>("Options", "B"),
            new KeyValuePair<string, string>("StandardAnswer", "A"),
            new KeyValuePair<string, string>("ReturnUrl", returnUrl),
            new KeyValuePair<string, string>("__RequestVerificationToken", editToken)
        });

        var editResponse = await _http.PostAsync($"/Questions/Edit/{questionId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        Assert.AreEqual(returnUrl, editResponse.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task UsersCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create User
        var userName = $"testuser{Guid.NewGuid().ToString().Substring(0, 8)}";
        var email = $"{userName}@test.com";
        var password = "TestPassword123!";
        var displayName = "Test User Display";
        
        var createToken = await GetAntiCsrfToken("/Users/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "UserName", userName },
            { "DisplayName", displayName },
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/Users/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        var userId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read User (Details)
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(userName, detailsHtml);
        Assert.Contains(email, detailsHtml);

        // 3. Edit User
        var newDisplayName = "Updated Display Name";
        var editToken = await GetAntiCsrfToken($"/Users/Edit/{userId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", userId },
            { "UserName", userName },
            { "Email", email },
            { "DisplayName", newDisplayName },
            { "AvatarUrl", "Workspace/avatar/default-avatar.jpg" },
            { "Password", "you-cant-read-it" }, // Keep password
            { "__RequestVerificationToken", editToken }
        });

        var editResponse = await _http.PostAsync($"/Users/Edit/{userId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync(detailsUrl);
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newDisplayName, verifyHtml);

        // 4. Delete User
        var deleteToken = await GetAntiCsrfToken($"/Users/Delete/{userId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        
        var deleteResponse = await _http.PostAsync($"/Users/Delete/{userId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Users/Index" || location == "/Users");

        // Verify Deletion
        var indexResponse = await _http.GetAsync("/Users/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(userName, indexHtml);
    }

    [TestMethod]
    public async Task ArticlesCrudTest()
    {
        await LoginAsAdminAsync();

        // 1. Create Article
        var title = $"Test-Article-{Guid.NewGuid()}";
        var content = "This is a test article content.";
        var createToken = await GetAntiCsrfToken("/Articles/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", title },
            { "Content", content },
            { "__RequestVerificationToken", createToken }
        });

        var createResponse = await _http.PostAsync("/Articles/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        
        var articleId = detailsUrl.Split('/').Last().Split('?')[0];

        // 2. Read Article
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(title, detailsHtml);
        Assert.Contains(content, detailsHtml);

        // 3. Edit Article
        var newTitle = $"Updated-Article-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/Articles/Edit/{articleId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", articleId },
            { "Title", newTitle },
            { "Content", content },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Articles/Edit/{articleId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Delete Article
        var deleteToken = await GetAntiCsrfToken($"/Articles/Delete/{articleId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Articles/Delete/{articleId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Articles/Index" || location == "/Articles");
    }

    [TestMethod]
    public async Task ExamsCrudTest()
    {
        await LoginAsAdminAsync();
        var snapshot = await PublishExamPaper(false);

        // 2. Create Exam
        var examTitle = $"Test-Exam-{Guid.NewGuid()}";
        var startTime = new DateTimeOffset(2030, 7, 1, 9, 30, 0, TimeSpan.FromHours(8));
        var endTime = startTime.AddDays(1);
        var createExamToken = await GetAntiCsrfToken("/Exams/Create");
        var createExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", examTitle },
            { "PaperSnapshotId", snapshot.Id.ToString() },
            { "StartTime", startTime.ToString("O") },
            { "EndTime", endTime.ToString("O") },
            { "DurationMinutes", "60" },
            { "__RequestVerificationToken", createExamToken }
        });
        var createExamResponse = await _http.PostAsync("/Exams/Create", createExamContent);
        Assert.AreEqual(HttpStatusCode.Found, createExamResponse.StatusCode);
        
        // 3. Read Exam (Index)
        var examIndexResponse = await _http.GetAsync("/Exams/Index");
        examIndexResponse.EnsureSuccessStatusCode();
        var examIndexHtml = await examIndexResponse.Content.ReadAsStringAsync();
        Assert.Contains(examTitle, examIndexHtml);

        var examIdMatch = Regex.Match(examIndexHtml, @"href=""/Exams/Edit/([a-z0-9-]+)");
        Assert.IsTrue(examIdMatch.Success);
        var examId = examIdMatch.Groups[1].Value;

        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var savedExam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == Guid.Parse(examId));
        Assert.IsNull(savedExam.PaperId);
        Assert.AreEqual(snapshot.Id, savedExam.PaperSnapshotId);
        Assert.AreEqual(startTime.UtcDateTime, savedExam.StartTime);
        Assert.AreEqual(endTime.UtcDateTime, savedExam.EndTime);
        var editHtml = await _http.GetStringAsync($"/Exams/Edit/{examId}");
        Assert.Contains("data-utc-input", editHtml);
        Assert.Contains("utc-input.js", editHtml);

        // 4. Edit Exam
        var newExamTitle = $"Updated-Exam-{Guid.NewGuid()}";
        var editExamToken = await GetAntiCsrfToken($"/Exams/Edit/{examId}");
        var editExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", examId },
            { "Title", newExamTitle },
            { "StartTime", startTime.ToString("O") },
            { "EndTime", endTime.ToString("O") },
            { "DurationMinutes", "90" },
            { "IsPublic", "true" },
            { "AllowedAttempts", "2" },
            { "AllowReview", "true" },
            { "ShowAnswerAfter", endTime.AddHours(1).ToString("O") },
            { "__RequestVerificationToken", editExamToken }
        });
        var editExamResponse = await _http.PostAsync($"/Exams/Edit/{examId}", editExamContent);
        Assert.AreEqual(HttpStatusCode.Found, editExamResponse.StatusCode);

        savedExam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == Guid.Parse(examId));
        Assert.AreEqual(startTime.UtcDateTime, savedExam.StartTime);
        Assert.AreEqual(endTime.UtcDateTime, savedExam.EndTime);
        Assert.AreEqual(endTime.AddHours(1).UtcDateTime, savedExam.ShowAnswerAfter);

        // Verify Edit
        var verifyExamResponse = await _http.GetAsync("/Exams/Index");
        var verifyExamHtml = await verifyExamResponse.Content.ReadAsStringAsync();
        Assert.Contains(newExamTitle, verifyExamHtml);

        // 5. Delete Exam
        var deleteExamToken = await GetAntiCsrfToken($"/Exams/Delete/{examId}");
        var deleteExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", examId },
            { "__RequestVerificationToken", deleteExamToken }
        });
        var deleteExamResponse = await _http.PostAsync($"/Exams/Delete/{examId}", deleteExamContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteExamResponse.StatusCode);
        
        // Verify Deletion
        var finalExamResponse = await _http.GetAsync("/Exams/Index");
        var finalExamHtml = await finalExamResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newExamTitle, finalExamHtml);
    }

    [TestMethod]
    public async Task CreateExamWithoutSnapshotShouldFailTest()
    {
        await LoginAsAdminAsync();

        var selectable = await PublishExamPaper(false);
        // A template ID is not a published version ID.
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var paper = new RealPaper { Title = "Unpublished paper", Institution = new Institution { Title = "Draft school" } };
        db.RealPapers.Add(paper);
        await db.SaveChangesAsync();

        // 2. Try to create Exam
        var examTitle = $"Fail-Exam-{Guid.NewGuid()}";
        var startTime = DateTime.Now.AddDays(1);
        var endTime = DateTime.Now.AddDays(2);
        var createExamToken = await GetAntiCsrfToken("/Exams/Create");
        var createExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", examTitle },
            { "PaperSnapshotId", paper.Id.ToString() },
            { "StartTime", startTime.ToString("yyyy-MM-ddTHH:mm") },
            { "EndTime", endTime.ToString("yyyy-MM-ddTHH:mm") },
            { "DurationMinutes", "60" },
            { "__RequestVerificationToken", createExamToken }
        });
        
        var createExamResponse = await _http.PostAsync("/Exams/Create", createExamContent);
        
        // It should NOT redirect (Found), but stay on the page (OK) with an error message.
        Assert.AreEqual(HttpStatusCode.OK, createExamResponse.StatusCode);
        var resultHtml = await createExamResponse.Content.ReadAsStringAsync();
        Assert.Contains("Select a published mock or real paper version with questions.", resultHtml);
        Assert.Contains($"value=\"{selectable.Id}\"", resultHtml);
    }

    private async Task<PaperSnapshot> PublishExamPaper(bool real)
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var category = QuestionPeriodAssignmentTests.TestPeriod();
        var question = new Question
        {
            Content = "Scheduled question", QuestionCategory = category, QuestionType = QuestionType.Choice,
            Metadata = "{\"options\":[\"A\",\"B\"]}", StandardAnswer = "A", GradingStrategy = GradingStrategy.ExactMatch
        };
        if (real)
        {
            var institution = new Institution { Title = "Scheduled school" };
            var paper = new RealPaper { Title = "Published real paper", TimeLimit = 150, Institution = institution, InstitutionId = institution.Id };
            db.RealPaperQuestions.Add(new RealPaperQuestion { RealPaper = paper, Question = question, Order = 1, Score = 10 });
            await db.SaveChangesAsync();
            return await scope.ServiceProvider.GetRequiredService<RealPaperPublishing>().PublishAsync(paper);
        }
        else
        {
            var paper = new MockPaper { Title = "Published mock paper", TimeLimit = 120, TotalCategory = category.TotalCategory!, TotalCategoryId = category.TotalCategoryId };
            db.MockPaperQuestions.Add(new MockPaperQuestion { MockPaper = paper, Question = question, Order = 1, Score = 10 });
            await db.SaveChangesAsync();
            return await scope.ServiceProvider.GetRequiredService<MockPaperPublishing>().PublishAsync(paper);
        }
    }

    [TestMethod]
    public async Task ExamSelectorUsesLatestPublishedVersionsAndRejectsUnavailableSources()
    {
        Assert.AreEqual(HttpStatusCode.Found, (await _http.GetAsync("/Exams/Create")).StatusCode);
        await LoginAsAdminAsync();
        var mock = await PublishExamPaper(false);
        var real = await PublishExamPaper(true);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IExamService>();
        var paper = await db.RealPapers.SingleAsync(p => p.Id == real.RealPaperId);
        paper.Title = "Updated real version";
        var latest = await scope.ServiceProvider.GetRequiredService<RealPaperPublishing>().PublishAsync(paper);
        paper.Status = PaperStatus.Frozen;
        var legacy = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Historical detached version", Version = 1, TimeLimit = 60 };
        db.PaperSnapshots.Add(legacy);
        await db.SaveChangesAsync();
        var choices = await service.GetPublishedPapersForExamAsync();
        Assert.IsTrue(choices.Any(p => p.Id == mock.Id));
        Assert.IsTrue(choices.Any(p => p.Id == latest.Id));
        Assert.IsFalse(choices.Any(p => p.Id == real.Id || p.Id == legacy.Id));
        var html = await _http.GetStringAsync("/Exams/Create");
        Assert.Contains($"value=\"{latest.Id}\"", html);
        Assert.Contains("Version 2", html);
        Assert.Contains("Mock paper", html);
        Assert.Contains("Real paper", html);
        Assert.DoesNotContain($"value=\"{real.Id}\"", html);
        paper.Status = PaperStatus.Draft;
        await db.SaveChangesAsync();
        Assert.IsFalse((await service.GetPublishedPapersForExamAsync()).Any(p => p.Id == latest.Id));
        foreach (var id in new[] { latest.Id, legacy.Id, Guid.NewGuid() })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateExamAsync("Unavailable", id,
                DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        }
        paper.Status = PaperStatus.Publishable;
        db.QuestionSnapshots.RemoveRange(await db.QuestionSnapshots.Where(q => q.PaperSnapshotId == latest.Id).ToListAsync());
        await db.SaveChangesAsync();
        Assert.IsFalse((await service.GetPublishedPapersForExamAsync()).Any(p => p.RealPaperId == paper.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateExamAsync("Empty", latest.Id,
            DateTime.UtcNow, DateTime.UtcNow.AddHours(1)));
        Assert.IsFalse(await db.Exams.AnyAsync(e => e.Title == "Unavailable" || e.Title == "Empty"));
    }

    [TestMethod]
    public async Task ScheduledExamKeepsChosenVersionAndSnapshotTitleAfterTemplateChanges()
    {
        await LoginAsAdminAsync();
        var first = await PublishExamPaper(true);
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IExamService>();
        var paper = await db.RealPapers.SingleAsync(p => p.Id == first.RealPaperId);
        paper.Title = "New template title";
        paper.TimeLimit = 60;
        await scope.ServiceProvider.GetRequiredService<RealPaperPublishing>().PublishAsync(paper);
        // A teacher's open form still selects version 1, even after version 2 is published.
        var token = await GetAntiCsrfToken("/Exams/Create");
        var response = await _http.PostAsync("/Exams/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Title"] = "Locked version exam", ["PaperSnapshotId"] = first.Id.ToString(),
            ["StartTime"] = DateTime.UtcNow.AddMinutes(-1).ToString("O"),
            ["EndTime"] = DateTime.UtcNow.AddHours(1).ToString("O"), ["__RequestVerificationToken"] = token
        }));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var exam = await db.Exams.SingleAsync(e => e.Title == "Locked version exam");
        _scheduledExamIds.Add(exam.Id);
        Assert.IsNull(exam.PaperId);
        Assert.AreEqual(first.Id, exam.PaperSnapshotId);
        Assert.AreEqual(150, exam.DurationMinutes);
        Assert.AreEqual(first.Title, (await service.GetExamAsync(exam.Id))!.PaperSnapshot!.Title);
        var edit = await _http.GetStringAsync($"/Exams/Edit/{exam.Id}");
        Assert.Contains(first.Title, edit);
        Assert.DoesNotContain("New template title", edit);
        Assert.Contains(first.Title, await _http.GetStringAsync("/Exams"));
        var user = await db.Users.FirstAsync();
        var record = await service.StartExamAsync(exam.Id, user.Id);
        Assert.AreEqual(first.Id, record.PaperSnapshotId);
        var question = await db.QuestionSnapshots.SingleAsync(q => q.PaperSnapshotId == first.Id);
        await service.SubmitAnswerAsync(record.Id, question.Id, "A");
        Assert.AreEqual(record.Id, (await service.StartExamAsync(exam.Id, user.Id)).Id);
        Assert.AreEqual("A", (await service.GetExamRecordAsync(record.Id))!.AnswerRecords.Single().UserAnswer);
        Assert.AreEqual(first.Title, (await service.GetAvailableExamsForUserAsync(user.Id)).Single(e => e.Id == exam.Id).PaperSnapshot!.Title);
    }

    [TestMethod]
    public async Task ExistingExamRetainsLockedSnapshotWhenLegacyTemplateReferenceIsRemoved()
    {
        using var scope = _server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var template = new Paper { Id = Guid.NewGuid(), Title = "Legacy template", Status = PaperStatus.Publishable, TimeLimit = 180 };
        var snapshot = new PaperSnapshot { Id = Guid.NewGuid(), Title = "Legacy locked title", Version = 3, TimeLimit = 180, Paper = template };
        var exam = new Exam { Id = Guid.NewGuid(), Title = "Historical exam", Paper = template, PaperSnapshot = snapshot,
            StartTime = DateTime.UtcNow.AddMinutes(-1), EndTime = DateTime.UtcNow.AddHours(1), DurationMinutes = 180, IsPublic = true };
        db.Exams.Add(exam);
        _scheduledExamIds.Add(exam.Id);
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<IExamService>();
        var userId = (await db.Users.FirstAsync()).Id;
        var record = await service.StartExamAsync(exam.Id, userId);
        exam.PaperId = null;
        exam.Paper = null;
        snapshot.PaperId = null;
        snapshot.Paper = null;
        db.Papers.Remove(template);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.AreEqual(record.Id, (await service.StartExamAsync(exam.Id, userId)).Id);
        Assert.AreEqual(snapshot.Id, (await service.GetExamRecordAsync(record.Id))!.PaperSnapshotId);
        Assert.AreEqual(snapshot.Title, (await service.GetAllExamsAsync()).Single(e => e.Id == exam.Id).PaperSnapshot!.Title);
    }

    [TestMethod]
    public async Task MtqlInvalidErrorShouldBeVisibleTest()
    {
        await LoginAsAdminAsync();

        // Access Questions Index with invalid MTQL
        var invalidMtql = "贝多芬 and 肖邦";
        var response = await _http.GetAsync($"/Questions/Index?mtql={Uri.EscapeDataString(invalidMtql)}");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        // Check if the error message is in the HTML
        // Expected message from Tokenizer: Unexpected keyword 'and' at index 4. Did you mean '&&'? ...
        Assert.IsTrue(html.Contains("Invalid MTQL: Unexpected keyword &#x27;and&#x27; at index 4. Did you mean &#x27;&amp;&amp;&#x27;?"), "Error message should be visible in HTML (with HTML encoding)");
    }

    [TestMethod]
    public async Task MtqlMissingOperatorErrorShouldBeVisibleTest()
    {
        await LoginAsAdminAsync();

        // Access Questions Index with invalid MTQL (missing operator)
        var invalidMtql = "贝多芬 肖邦";
        var response = await _http.GetAsync($"/Questions/Index?mtql={Uri.EscapeDataString(invalidMtql)}");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();

        // Expected message from AstBuilder: Missing operator between tags. If you meant to join them, use '&&' or '||'. ...
        Assert.IsTrue(html.Contains("Invalid MTQL: Missing operator between tags. If you meant to join them, use &#x27;&amp;&amp;&#x27; or &#x27;||&#x27;."), "Error message should be visible in HTML (with HTML encoding)");
    }

    [TestMethod]
    public async Task MtqlSearchActionErrorShouldBeJsonTest()
    {
        await LoginAsAdminAsync();

        var invalidMtql = "贝多芬 and 肖邦";
        var response = await _http.GetAsync($"/Questions/Search?mtql={Uri.EscapeDataString(invalidMtql)}");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        // Check if it's JSON and contains the message property
        Assert.IsTrue(json.Contains("\"message\":\"Invalid MTQL: Unexpected keyword 'and' at index 4. Did you mean '&&'?"), "JSON error should contain the message property.");
    }
}
