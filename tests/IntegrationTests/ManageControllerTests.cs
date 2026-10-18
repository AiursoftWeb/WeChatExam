using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Configuration;
using System.Net;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ManageControllerTests : TestBase
{
    [TestMethod]
    public async Task SecretSettingsPreserveLegacyValuesAndHideSavedSecrets()
    {
        const string secretKey = "IntegrationTest_Secret";
        const string textKey = "IntegrationTest_LegacyText";
        const string legacySecret = "legacy-test-secret-value";
        const string legacyText = "protected:v1:literal-text-value";
        var secretDefinition = new Models.GlobalSettingDefinition
        {
            Key = secretKey, Name = "Test secret", Description = "Test only", DefaultValue = "",
            Type = Models.SettingType.Secret
        };
        var textDefinition = new Models.GlobalSettingDefinition
        {
            Key = textKey, Name = "Test text", Description = "Test only", DefaultValue = "",
            Type = Models.SettingType.Text
        };
        SettingsMap.Definitions.Add(secretDefinition);
        SettingsMap.Definitions.Add(textDefinition);
        using var scope = Server!.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        try
        {
            var settings = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.GlobalSettings.Add(new GlobalSetting { Key = secretKey, Value = legacySecret });
            db.GlobalSettings.Add(new GlobalSetting { Key = textKey, Value = legacyText });
            await db.SaveChangesAsync();
            Assert.AreEqual(legacySecret, await settings.GetSettingValueAsync(secretKey));
            Assert.AreEqual(legacyText, await settings.GetSettingValueAsync(textKey));
            await settings.SeedSettingsAsync();
            Assert.AreEqual(legacySecret, (await db.GlobalSettings.AsNoTracking().SingleAsync(s => s.Key == secretKey)).Value);
            Assert.AreEqual(legacyText, (await db.GlobalSettings.AsNoTracking().SingleAsync(s => s.Key == textKey)).Value);
            await settings.UpdateSettingAsync(textKey, legacyText);
            Assert.AreEqual(legacyText, (await db.GlobalSettings.AsNoTracking().SingleAsync(s => s.Key == textKey)).Value);
            await LoginAsAdmin();
            Assert.DoesNotContain(legacySecret, await Http.GetStringAsync("/GlobalSettings/Index"));
            await settings.UpdateSettingAsync(secretKey, "replacement-test-secret");
            var storedValue = (await db.GlobalSettings.AsNoTracking().SingleAsync(s => s.Key == secretKey)).Value!;
            Assert.StartsWith("protected:v1:", storedValue);
            Assert.DoesNotContain("replacement-test-secret", storedValue);
            Assert.AreEqual("replacement-test-secret", await settings.GetSettingValueAsync(secretKey));
            var html = await Http.GetStringAsync("/GlobalSettings/Index");
            Assert.Contains(secretKey, html);
            Assert.DoesNotContain("replacement-test-secret", html);
            Assert.DoesNotContain(storedValue, html);
            configuration[$"GlobalSettings:{secretKey}"] = "override-test-secret";
            cache.Remove($"global-setting-{secretKey}");
            Assert.AreEqual("override-test-secret", await settings.GetSettingValueAsync(secretKey));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => settings.UpdateSettingAsync(secretKey, "replacement"));
            Assert.DoesNotContain("override-test-secret", await Http.GetStringAsync("/GlobalSettings/Index"));
            configuration[$"GlobalSettings:{secretKey}"] = null;
            cache.Remove($"global-setting-{secretKey}");
            await settings.UpdateSettingAsync(secretKey, "");
            Assert.AreEqual("", await settings.GetSettingValueAsync(secretKey));
        }
        finally
        {
            configuration[$"GlobalSettings:{secretKey}"] = null;
            cache.Remove($"global-setting-{secretKey}");
            cache.Remove($"global-setting-{textKey}");
            SettingsMap.Definitions.Remove(secretDefinition);
            SettingsMap.Definitions.Remove(textDefinition);
        }
    }

    [TestMethod]
    [Ignore("InMemory database does not enforce FK constraints. DeleteBehavior.Restrict requires a real DB (Sqlite/MySQL) to throw DbUpdateException.")]
    public async Task TestDeleteAccount_WithAssets_Blocked()
    {
        // Arrange: register, login, and create a financial record owned by the user
        var (email, _) = await RegisterAndLoginAsync();

        string userId;
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.PaymentOrders.Add(new PaymentOrder
            {
                OutTradeNo = $"TEST-{Guid.NewGuid():N}",
                UserId = userId,
                Description = "Test payment for account deletion UT"
            });
            await db.SaveChangesAsync();
        }

        // Act: try to delete account — will hit DB Restrict constraint
        var deleteResponse = await PostForm("/Manage/DeleteAccountPost", new(),
            tokenUrl: "/Manage/ChangePassword");

        // Assert: stays on page with error (not redirected to "/")
        Assert.AreEqual(HttpStatusCode.OK, deleteResponse.StatusCode);
        var html = await deleteResponse.Content.ReadAsStringAsync();
        Assert.Contains("Cannot delete", html);

        // Assert: user still exists
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.IsNotNull(await userManager.FindByEmailAsync(email));
        }

        // Assert: asset still exists
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            Assert.IsTrue(await db.PaymentOrders.AnyAsync(p => p.UserId == userId));
        }
    }

    [TestMethod]
    public async Task TestDeleteAccount_NoAssets_Succeeds()
    {
        // Arrange: register and login (no assets created)
        var (email, _) = await RegisterAndLoginAsync();

        // Act: confirmation page loads
        var deletePage = await Http.GetAsync("/Manage/DeleteAccount");
        deletePage.EnsureSuccessStatusCode();

        // Act: confirm deletion
        var deleteResponse = await PostForm("/Manage/DeleteAccountPost", new(),
            tokenUrl: "/Manage/DeleteAccount");
        AssertRedirect(deleteResponse, "/");

        // Assert: signed out
        var managePage = await Http.GetAsync("/Manage/Index");
        Assert.AreEqual(HttpStatusCode.Found, managePage.StatusCode);

        // Assert: user gone from DB
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.IsNull(await userManager.FindByEmailAsync(email));
    }

    [TestMethod]
    public async Task TestDeleteAccount_Unauthenticated_RedirectsToLogin()
    {
        var deletePage = await Http.GetAsync("/Manage/DeleteAccount");
        Assert.AreEqual(HttpStatusCode.Found, deletePage.StatusCode);
    }
}
