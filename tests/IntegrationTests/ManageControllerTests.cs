using System.Net;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class ManageControllerTests : TestBase
{
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
