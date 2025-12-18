using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// 基础集成测试：测试管理员账号的注册、登录、登出、修改密码、修改资料等核心功能
/// </summary>
[TestClass]
public class BasicTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public BasicTests()
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
        await _server.UpdateDbAsync<TemplateDbContext>();
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

    /// <summary>
    /// 从页面HTML中提取反CSRF令牌
    /// </summary>
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

    [TestMethod]
    [DataRow("/")]
    [DataRow("/Home?test=value")]
    [DataRow("/Home/Index")]
    public async Task GetHome(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task RegisterAndLoginAndLogOffTest()
    {
        var expectedUserName = $"test-{Guid.NewGuid()}";
        var email = $"{expectedUserName}@aiursoft.com";
        var password = "Test-Password-123";

        // Step 1: 注册新用户并确认重定向成功
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);
        Assert.AreEqual("/Dashboard/Index", registerResponse.Headers.Location?.OriginalString);

        // Step 2: 登出用户并确认重定向成功
        var homePageResponse = await _http.GetAsync("/Manage/Index");
        homePageResponse.EnsureSuccessStatusCode();
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        var logOffResponse = await _http.PostAsync("/Account/LogOff", logOffContent);
        Assert.AreEqual(HttpStatusCode.Found, logOffResponse.StatusCode);
        Assert.AreEqual("/", logOffResponse.Headers.Location?.OriginalString);

        // Step 3: 使用刚创建的账号登录并确认重定向成功
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

        // Step 4: 验证最终的登录状态，检查首页内容
        var finalHomePageResponse = await _http.GetAsync("/dashboard/index");
        finalHomePageResponse.EnsureSuccessStatusCode();
        var finalHtml = await finalHomePageResponse.Content.ReadAsStringAsync();
        Assert.Contains(expectedUserName, finalHtml);
    }

    [TestMethod]
    public async Task LoginWithInvalidCredentialsTest()
    {
        // Step 1: 尝试使用不存在的账号登录
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Wrong-Password-123";
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);

        // Step 2: 确认登录失败，并显示正确的错误信息
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", html);
    }

    [TestMethod]
    public async Task RegisterWithExistingEmailTest()
    {
        // Step 1: 成功注册一个新用户
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Step 2: 登出以清除当前会话
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);

        // Step 3: 尝试使用相同的邮箱再次注册
        var secondRegisterToken = await GetAntiCsrfToken("/Account/Register");
        var secondRegisterContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", secondRegisterToken }
        });
        var secondRegisterResponse = await _http.PostAsync("/Account/Register", secondRegisterContent);

        // Step 4: 确认注册失败，并显示正确的错误信息
        Assert.AreEqual(HttpStatusCode.OK, secondRegisterResponse.StatusCode);
        var html = await secondRegisterResponse.Content.ReadAsStringAsync();
        Assert.Contains("The username already exists. Please try another username.", html);
    }

    [TestMethod]
    public async Task LoginWithExistingUserAndWrongPasswordTest()
    {
        // Step 1: 注册一个新用户
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var correctPassword = "Test-Password-123";
        var wrongPassword = "Wrong-Password-456";
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", correctPassword },
            { "ConfirmPassword", correctPassword },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Step 2: 登出用户
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);

        // Step 3: 尝试使用正确的邮箱和错误的密码登录
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", wrongPassword },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);

        // Step 4: 确认登录失败，并显示错误信息
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("Invalid login attempt", html);
    }

    [TestMethod]
    public async Task AccountLockoutTest()
    {
        // ASP.NET Core Identity 的默认最大失败登录次数
        const int maxFailedAccessAttempts = 5;

        // Step 1: 注册一个新用户，然后登出
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var correctPassword = "Test-Password-123";
        var wrongPassword = "Wrong-Password-456";
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", correctPassword },
            { "ConfirmPassword", correctPassword },
            { "__RequestVerificationToken", registerToken }
        });
        await _http.PostAsync("/Account/Register", registerContent);
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);

        // Step 2: 多次使用错误密码登录，触发账号锁定
        HttpResponseMessage loginResponse = null!;
        for (int i = 0; i < maxFailedAccessAttempts; i++)
        {
            var loginToken = await GetAntiCsrfToken("/Account/Login");
            var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "EmailOrUserName", email },
                { "Password", wrongPassword },
                { "__RequestVerificationToken", loginToken }
            });
            loginResponse = await _http.PostAsync("/Account/Login", loginContent);
        }

        // Step 3: 确认账号已被锁定
        Assert.AreEqual(HttpStatusCode.OK, loginResponse.StatusCode);
        var html = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("This account has been locked out, please try again later.", html);

        // Step 4: 验证即使使用正确的密码也无法登录（因为账号被锁定）
        var finalLoginToken = await GetAntiCsrfToken("/Account/Login");
        var finalLoginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", correctPassword },
            { "__RequestVerificationToken", finalLoginToken }
        });
        var finalLoginResponse = await _http.PostAsync("/Account/Login", finalLoginContent);
        var finalHtml = await finalLoginResponse.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, finalLoginResponse.StatusCode);
        Assert.Contains("This account has been locked out, please try again later.", finalHtml);
    }

    /// <summary>
    /// 注册并登录一个新用户，返回邮箱和密码供后续测试使用
    /// </summary>
    private async Task<(string email, string password)> RegisterAndLoginAsync()
    {
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        // Step 1: 注册新用户
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);

        // Step 2: 确认注册成功并已登录
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        return (email, password);
    }

    [TestMethod]
    public async Task ChangePasswordSuccessfullyTest()
    {
        // Step 1: 注册并登录一个新用户，保存其凭证以供整个测试使用
        var (email, oldPassword) = await RegisterAndLoginAsync();
        var newPassword = "New-Password-456";

        // Step 2: 从修改密码页面获取反CSRF令牌
        var changePasswordToken = await GetAntiCsrfToken("/Manage/ChangePassword");

        // Step 3: 提交表单以修改密码
        var changePasswordContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "OldPassword", oldPassword },
            { "NewPassword", newPassword },
            { "ConfirmPassword", newPassword },
            { "__RequestVerificationToken", changePasswordToken }
        });
        var changePasswordResponse = await _http.PostAsync("/Manage/ChangePassword", changePasswordContent);

        // Step 4: 确认密码修改成功并正确重定向
        Assert.AreEqual(HttpStatusCode.Found, changePasswordResponse.StatusCode);
        Assert.AreEqual("/Manage?Message=ChangePasswordSuccess",
            changePasswordResponse.Headers.Location?.OriginalString);

        // Step 5: 登出以测试新密码
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);

        // Step 6: 验证旧密码不再有效
        var oldLoginToken = await GetAntiCsrfToken("/Account/Login");
        var oldLoginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", oldPassword },
            { "__RequestVerificationToken", oldLoginToken }
        });
        var oldLoginResponse = await _http.PostAsync("/Account/Login", oldLoginContent);
        Assert.AreEqual(HttpStatusCode.OK, oldLoginResponse.StatusCode);

        // Step 7: 验证新密码可以正常使用
        var newLoginToken = await GetAntiCsrfToken("/Account/Login");
        var newLoginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", newPassword },
            { "__RequestVerificationToken", newLoginToken }
        });
        var newLoginResponse = await _http.PostAsync("/Account/Login", newLoginContent);
        Assert.AreEqual(HttpStatusCode.Found, newLoginResponse.StatusCode);
    }

    [TestMethod]
    public async Task ChangeProfileSuccessfullyTest()
    {
        // Step 1: 注册并登录一个新用户
        var (email, _) = await RegisterAndLoginAsync();
        var originalUserName = email.Split('@')[0];
        var newUserName = $"new-name-{new Random().Next(1000, 9999)}";

        // Step 2: 从修改资料页面获取反CSRF令牌
        var changeProfileToken = await GetAntiCsrfToken("/Manage/ChangeProfile");

        // Step 3: 提交表单以修改用户显示名称
        var changeProfileContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Name", newUserName },
            { "__RequestVerificationToken", changeProfileToken }
        });
        var changeProfileResponse = await _http.PostAsync("/Manage/ChangeProfile", changeProfileContent);

        // Step 4: 确认资料修改成功并正确重定向
        Assert.AreEqual(HttpStatusCode.Found, changeProfileResponse.StatusCode);
        Assert.AreEqual("/Manage?Message=ChangeProfileSuccess", changeProfileResponse.Headers.Location?.OriginalString);

        // Step 5: 访问首页并验证新名称已显示
        var homePageResponse = await _http.GetAsync("/dashboard/index");
        homePageResponse.EnsureSuccessStatusCode();
        var html = await homePageResponse.Content.ReadAsStringAsync();
        Assert.Contains(newUserName, html);
        Assert.DoesNotContain(originalUserName, html);
    }

    [TestMethod]
    public async Task ChangePasswordWithWrongOldPasswordTest()
    {
        // Step 1: 注册并登录一个新用户
        await RegisterAndLoginAsync();


        var wrongOldPassword = "Wrong-Old-Password-999";
        var newPassword = "New-Password-456";

        // Step 2: 从修改密码页面获取反CSRF令牌
        var changePasswordToken = await GetAntiCsrfToken("/Manage/ChangePassword");

        // Step 3: 使用错误的旧密码提交表单
        var changePasswordContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "OldPassword", wrongOldPassword },
            { "NewPassword", newPassword },
            { "ConfirmPassword", newPassword },
            { "__RequestVerificationToken", changePasswordToken }
        });
        var changePasswordResponse = await _http.PostAsync("/Manage/ChangePassword", changePasswordContent);

        // Step 4: 确认密码修改失败
        Assert.AreEqual(HttpStatusCode.OK, changePasswordResponse.StatusCode);
        var html = await changePasswordResponse.Content.ReadAsStringAsync();
        // 验证页面包含错误信息（Identity的默认错误消息）
        Assert.Contains("Incorrect password", html, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public async Task LoginWithUsernameTest()
    {
        // Step 1: 注册一个新用户
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var userName = email.Split('@')[0];
        var password = "Test-Password-123";
        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        await _http.PostAsync("/Account/Register", registerContent);

        // Step 2: 登出
        var logOffToken = await GetAntiCsrfToken("/Manage/ChangePassword");
        var logOffContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", logOffToken }
        });
        await _http.PostAsync("/Account/LogOff", logOffContent);

        // Step 3: 使用用户名（而不是邮箱）登录
        var loginToken = await GetAntiCsrfToken("/Account/Login");
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "EmailOrUserName", userName }, // 使用用户名
            { "Password", password },
            { "__RequestVerificationToken", loginToken }
        });
        var loginResponse = await _http.PostAsync("/Account/Login", loginContent);

        // Step 4: 确认登录成功
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
        Assert.AreEqual("/Dashboard/Index", loginResponse.Headers.Location?.OriginalString);
    }
}
