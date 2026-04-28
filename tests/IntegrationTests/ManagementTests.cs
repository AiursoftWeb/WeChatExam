using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ManagementTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ManagementTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<Startup>([], port: _port);
        await _server.UpdateDbAsync<WeChatExamDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
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

        // 1. Create Category
        var categoryTitle = $"Test-Category-{Guid.NewGuid()}";
        var createToken = await GetAntiCsrfToken("/Categories/Create");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", createToken }
        });
        
        var createResponse = await _http.PostAsync("/Categories/Create", createContent);
        Assert.AreEqual(HttpStatusCode.Found, createResponse.StatusCode);
        var detailsUrl = createResponse.Headers.Location?.OriginalString;
        Assert.IsNotNull(detailsUrl);
        Assert.StartsWith("/Categories/Details", detailsUrl);
        
        var categoryId = detailsUrl.Split('/').Last().Split('?')[0]; // Extract ID

        // 2. Read Category (Details)
        var detailsResponse = await _http.GetAsync(detailsUrl);
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(categoryTitle, detailsHtml);

        // 3. Edit Category
        var newTitle = $"Updated-Category-{Guid.NewGuid()}";
        var editToken = await GetAntiCsrfToken($"/Categories/Edit/{categoryId}");
        var editContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", categoryId },
            { "Title", newTitle },
            { "__RequestVerificationToken", editToken }
        });
        var editResponse = await _http.PostAsync($"/Categories/Edit/{categoryId}", editContent);
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);
        
        // Verify Edit
        var verifyResponse = await _http.GetAsync(detailsUrl);
        var verifyHtml = await verifyResponse.Content.ReadAsStringAsync();
        Assert.Contains(newTitle, verifyHtml);

        // 4. Delete Category
        var deleteToken = await GetAntiCsrfToken($"/Categories/Delete/{categoryId}");
        var deleteContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", deleteToken }
        });
        var deleteResponse = await _http.PostAsync($"/Categories/Delete/{categoryId}", deleteContent);
        Assert.AreEqual(HttpStatusCode.Found, deleteResponse.StatusCode);
        var location = deleteResponse.Headers.Location?.OriginalString;
        Assert.IsTrue(location == "/Categories/Index" || location == "/Categories");

        // Verify Deletion
        var indexResponse = await _http.GetAsync("/Categories/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newTitle, indexHtml);
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
        await LoginAsAdminAsync();

        // Prerequisite: Create a Category first
        var categoryTitle = $"Q-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        var catContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        });
        var catResponse = await _http.PostAsync("/Categories/Create", catContent);
        var catDetailsUrl = catResponse.Headers.Location?.OriginalString;
        var categoryId = catDetailsUrl!.Split('/').Last().Split('?')[0];

        // 1. Create Question
        var qText = $"Test Question {Guid.NewGuid()}";
        var qType = "0";
        var qStrategy= "0";
        var createToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        var createContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", qType },
            { "GradingStrategy", qStrategy },
            { "CategoryId", categoryId },
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
            { "CategoryId", categoryId },
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
        await LoginAsAdminAsync();

        // 1. Setup: Create a Category and a Question
        var categoryTitle = $"Nav-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        }));

        var catIndexResponse = await _http.GetAsync("/Categories/Index");
        var catIndexHtml = await catIndexResponse.Content.ReadAsStringAsync();
        var categoryId = Regex.Match(catIndexHtml, @"href=""/Categories/Details/([a-z0-9-]+)").Groups[1].Value;

        var qToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", "Navigation Test Question" },
            { "QuestionType", "0" },
            { "GradingStrategy", "0" },
            { "CategoryId", categoryId },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "__RequestVerificationToken", qToken }
        }));

        var qIndexUrl = $"/Questions/Index?categoryId={categoryId}&page=1";
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
            new KeyValuePair<string, string>("CategoryId", categoryId),
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

        // 1. Setup prerequisite: Category, Question, Paper, and Publish
        var categoryTitle = $"Exam-Prereq-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        }));
        var catIndexResponse = await _http.GetAsync("/Categories/Index");
        var catIndexHtml = await catIndexResponse.Content.ReadAsStringAsync();
        var categoryId = Regex.Match(catIndexHtml, @"href=""/Categories/Details/([a-z0-9-]+)").Groups[1].Value;

        var qText = $"Exam-Prereq-Q-{Guid.NewGuid()}";
        var qToken = await GetAntiCsrfToken($"/Questions/Create?categoryId={categoryId}");
        await _http.PostAsync("/Questions/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Content", qText},
            { "QuestionType", "0" },
            { "GradingStrategy", "0" },
            { "CategoryId", categoryId },
            { "Options[0]", "A" },
            { "Options[1]", "B" },
            { "StandardAnswer", "A" },
            { "Explanation", "A is correct" },
            { "__RequestVerificationToken", qToken }
        }));
        var qIndexResponse = await _http.GetAsync($"/Questions/Index?categoryId={categoryId}");
        var qIndexHtml = await qIndexResponse.Content.ReadAsStringAsync();
        var questionId = Regex.Match(qIndexHtml, @"href=""/Questions/Details/([a-z0-9-]+)").Groups[1].Value;

        var paperTitle = $"Exam-Prereq-Paper-{Guid.NewGuid()}";
        var pToken = await GetAntiCsrfToken("/Papers/Create");
        var pCreateResponse = await _http.PostAsync("/Papers/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", paperTitle },
            { "TimeLimit", "60" },
            { "IsFree", "true" },
            { "SelectedCategoryId", categoryId },
            { "__RequestVerificationToken", pToken }
        }));
        var paperEditUrl = pCreateResponse.Headers.Location?.OriginalString;
        var paperId = paperEditUrl!.Split('/').Last().Split('?')[0];

        var addQToken = await GetAntiCsrfToken(paperEditUrl);
        await _http.PostAsync($"/Papers/AddQuestion/{paperId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "questionId", questionId },
            { "order", "1" },
            { "score", "10" },
            { "__RequestVerificationToken", addQToken }
        }));

        var publishableToken = await GetAntiCsrfToken(paperEditUrl);
        await _http.PostAsync($"/Papers/SetStatus/{paperId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "status", "1" }, // Publishable
            { "__RequestVerificationToken", publishableToken }
        }));

        var publishToken = await GetAntiCsrfToken(paperEditUrl);
        await _http.PostAsync($"/Papers/Publish/{paperId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", publishToken }
        }));

        // 2. Create Exam
        var examTitle = $"Test-Exam-{Guid.NewGuid()}";
        var startTime = DateTime.Now.AddDays(1);
        var endTime = DateTime.Now.AddDays(2);
        var createExamToken = await GetAntiCsrfToken("/Exams/Create");
        var createExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", examTitle },
            { "PaperId", paperId },
            { "StartTime", startTime.ToString("yyyy-MM-ddTHH:mm") },
            { "EndTime", endTime.ToString("yyyy-MM-ddTHH:mm") },
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

        // 4. Edit Exam
        var newExamTitle = $"Updated-Exam-{Guid.NewGuid()}";
        var editExamToken = await GetAntiCsrfToken($"/Exams/Edit/{examId}");
        var editExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Id", examId },
            { "Title", newExamTitle },
            { "StartTime", startTime.ToString("yyyy-MM-ddTHH:mm") },
            { "EndTime", endTime.ToString("yyyy-MM-ddTHH:mm") },
            { "DurationMinutes", "90" },
            { "IsPublic", "true" },
            { "AllowedAttempts", "2" },
            { "AllowReview", "true" },
            { "__RequestVerificationToken", editExamToken }
        });
        var editExamResponse = await _http.PostAsync($"/Exams/Edit/{examId}", editExamContent);
        Assert.AreEqual(HttpStatusCode.Found, editExamResponse.StatusCode);

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

        // 1. Setup prerequisite: Category and Paper (but NO publish)
        var categoryTitle = $"Fail-Exam-Cat-{Guid.NewGuid()}";
        var catToken = await GetAntiCsrfToken("/Categories/Create");
        await _http.PostAsync("/Categories/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", categoryTitle },
            { "__RequestVerificationToken", catToken }
        }));
        var catIndexResponse = await _http.GetAsync("/Categories/Index");
        var catIndexHtml = await catIndexResponse.Content.ReadAsStringAsync();
        var categoryId = Regex.Match(catIndexHtml, @"href=""/Categories/Details/([a-z0-9-]+)").Groups[1].Value;

        var paperTitle = $"Unpublished-Paper-{Guid.NewGuid()}";
        var pToken = await GetAntiCsrfToken("/Papers/Create");
        var pCreateResponse = await _http.PostAsync("/Papers/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", paperTitle },
            { "TimeLimit", "60" },
            { "IsFree", "true" },
            { "SelectedCategoryId", categoryId },
            { "__RequestVerificationToken", pToken }
        }));
        var paperEditUrl = pCreateResponse.Headers.Location?.OriginalString;
        var paperId = paperEditUrl!.Split('/').Last().Split('?')[0];

        // 2. Try to create Exam
        var examTitle = $"Fail-Exam-{Guid.NewGuid()}";
        var startTime = DateTime.Now.AddDays(1);
        var endTime = DateTime.Now.AddDays(2);
        var createExamToken = await GetAntiCsrfToken("/Exams/Create");
        var createExamContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Title", examTitle },
            { "PaperId", paperId },
            { "StartTime", startTime.ToString("yyyy-MM-ddTHH:mm") },
            { "EndTime", endTime.ToString("yyyy-MM-ddTHH:mm") },
            { "DurationMinutes", "60" },
            { "__RequestVerificationToken", createExamToken }
        });
        
        var createExamResponse = await _http.PostAsync("/Exams/Create", createExamContent);
        
        // It should NOT redirect (Found), but stay on the page (OK) with an error message.
        Assert.AreEqual(HttpStatusCode.OK, createExamResponse.StatusCode);
        var resultHtml = await createExamResponse.Content.ReadAsStringAsync();
        Assert.Contains("The selected paper has no published snapshots", resultHtml);
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
