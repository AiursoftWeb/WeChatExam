using System.Net;
using System.Security.Claims;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CouponAmountTests : TestBase
{
    private readonly Guid _channel = Guid.NewGuid();
    private readonly Guid _coupon = Guid.NewGuid();

    private async Task Seed()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        db.AddRange(new Coupon { Id = _coupon, Code = "AMOUNT-EXAMPLE", AmountInFen = 1234, IsEnabled = false, IsSingleUse = true,
                DistributionChannel = new DistributionChannel { Id = _channel, AgencyName = "Amount agency" } },
            new VipProduct { Name = "Course", PriceInFen = 5000 });
        await db.SaveChangesAsync();
    }

    private string Url(Guid? channel = null) => $"/DistributionChannels/EditCouponAmount/{_coupon}?channelId={channel ?? _channel}";
    private Task<HttpResponseMessage> Save(string? amount, bool csrf = true, Guid? channel = null) =>
        PostForm(Url(channel), amount == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["AmountInFen"] = amount },
            tokenUrl: Url(), includeToken: csrf);

    [TestMethod]
    public async Task EditorShowsFenAndChangesOnlyTheDiscount()
    {
        await Seed(); await LoginAsAdmin();
        var html = await Http.GetStringAsync(Url());
        Assert.Contains("1234", html);
        Assert.Contains("including coupons already claimed", html);
        var channel = await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}");
        Assert.Contains($"/DistributionChannels/EditCouponAmount/{_coupon}", channel);
        using var saved = await Save("3005");
        AssertRedirect(saved, $"/DistributionChannels/Details/{_channel}");
        Assert.Contains("existing orders and purchase history are unchanged", await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}"));
        using var scope = Server!.Services.CreateScope();
        var coupon = await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Coupons.FindAsync(_coupon);
        Assert.AreEqual(3005, coupon!.AmountInFen);
        Assert.IsFalse(coupon.IsEnabled);
        Assert.IsTrue(coupon.IsSingleUse);
        Assert.AreEqual("AMOUNT-EXAMPLE", coupon.Code);
        using var zero = await Save("0");
        Assert.AreEqual(HttpStatusCode.Found, zero.StatusCode);
    }

    [TestMethod]
    public async Task InvalidAmountsKeepTheEditorAndStoredAmount()
    {
        await Seed(); await LoginAsAdmin();
        foreach (var amount in new[] { null, "not-money", "-1", "1.001", "2147483648", "5001" })
        {
            using var response = await Save(amount);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, amount);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.IsTrue(html.Contains("field-validation-error") || html.Contains("validation-summary-errors"), amount);
            Assert.Contains("AMOUNT-EXAMPLE", html);
            Assert.Contains($"/DistributionChannels/Details/{_channel}", html);
        }
        using var scope = Server!.Services.CreateScope();
        Assert.AreEqual(1234, (await scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Coupons.FindAsync(_coupon))!.AmountInFen);
    }

    [TestMethod]
    public async Task EditingRequiresCorrectOwnershipCsrfAndEditPermission()
    {
        await Seed(); await LoginAsAdmin();
        using var foreignGet = await Http.GetAsync(Url(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.NotFound, foreignGet.StatusCode);
        using var foreignPost = await Save("5", channel: Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.NotFound, foreignPost.StatusCode);
        using var noCsrf = await Save("5", csrf: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadDistributionChannels));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        Assert.DoesNotContain("EditCouponAmount", await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}"));
        using var deniedGet = await Http.GetAsync(Url());
        Assert.IsTrue(deniedGet.StatusCode == HttpStatusCode.Forbidden || deniedGet.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        using var deniedPost = await PostForm(Url(), new Dictionary<string, string> { ["AmountInFen"] = "5" }, tokenUrl: $"/DistributionChannels/Details/{_channel}");
        Assert.IsTrue(deniedPost.StatusCode == HttpStatusCode.Forbidden || deniedPost.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        using var final = Server!.Services.CreateScope();
        Assert.AreEqual(1234, (await final.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Coupons.FindAsync(_coupon))!.AmountInFen);
    }
}
