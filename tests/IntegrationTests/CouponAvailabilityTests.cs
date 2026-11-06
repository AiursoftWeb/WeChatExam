using System.Net;
using System.Security.Claims;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CouponAvailabilityTests : TestBase
{
    private readonly Guid _channel = Guid.NewGuid();
    private readonly Guid _coupon = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();

    private async Task Seed()
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var channel = new DistributionChannel { Id = _channel, AgencyName = "Availability agency" };
        db.AddRange(new Coupon { Id = _coupon, Code = _coupon.ToString("N").ToUpperInvariant(), DistributionChannel = channel, AmountInFen = 100 },
            new Coupon { Id = _other, Code = _other.ToString("N").ToUpperInvariant(), DistributionChannel = channel, AmountInFen = 200 });
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> SetState(string? enabled, Guid? channel = null, bool review = false, bool csrf = true)
    {
        var data = new Dictionary<string, string> { ["returnToReview"] = review.ToString() };
        if (enabled != null) data["isEnabled"] = enabled;
        return PostForm($"/DistributionChannels/SetCouponEnabled/{_coupon}?channelId={channel ?? _channel}", data,
            tokenUrl: $"/DistributionChannels/Details/{_channel}", includeToken: csrf);
    }

    [TestMethod]
    public async Task DesiredStateIsRepeatableAndDoesNotDisableOtherCouponsOrTheChannel()
    {
        await Seed(); await LoginAsAdmin();
        var initial = await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}");
        Assert.Contains("name=\"isEnabled\" value=\"false\"", initial);
        for (var i = 0; i < 2; i++)
        {
            using var response = await SetState("false");
            AssertRedirect(response, $"/DistributionChannels/Details/{_channel}");
        }
        var html = await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}");
        Assert.Contains("Coupon disabled. Existing claims, orders and memberships have been kept.", html);
        Assert.Contains("Enable Coupon", html);
        Assert.Contains("name=\"isEnabled\" value=\"true\"", html);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsFalse((await db.Coupons.FindAsync(_coupon))!.IsEnabled);
        Assert.IsTrue((await db.Coupons.FindAsync(_other))!.IsEnabled);
        Assert.IsTrue((await db.DistributionChannels.FindAsync(_channel))!.IsEnabled);
        using var enabled = await SetState("true", review: true);
        AssertRedirect(enabled, $"/DistributionChannels/DeleteCoupon/{_coupon}?channelId={_channel}");
        db.ChangeTracker.Clear();
        Assert.IsTrue((await db.Coupons.FindAsync(_coupon))!.IsEnabled);
    }

    [TestMethod]
    public async Task DisabledChannelRemainsUnavailableWhenOneCouponIsEnabled()
    {
        await Seed(); await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        (await db.DistributionChannels.FindAsync(_channel))!.IsEnabled = false;
        await db.SaveChangesAsync();
        using var response = await SetState("true");
        AssertRedirect(response, $"/DistributionChannels/Details/{_channel}");
        var html = await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}");
        Assert.Contains("It remains unavailable until the channel is enabled.", html);
        Assert.Contains("Channel Disabled", html);
        Assert.Contains("alert-warning", html);
        var service = scope.ServiceProvider.GetRequiredService<ICouponService>();
        Assert.AreEqual("该分销渠道已关闭", (await service.ValidateCouponAsync(_coupon.ToString("N"), Guid.NewGuid(), "new-buyer")).ErrorMessage);
        Assert.IsFalse((await service.ClaimCouponAsync("new-buyer", _coupon.ToString("N"))).Success);
    }

    [TestMethod]
    public async Task StateChangeRequiresValidInputChannelAndCsrf()
    {
        await Seed(); await LoginAsAdmin();
        foreach (var value in new[] { null, "not-a-boolean" })
        {
            using var invalid = await SetState(value);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var foreign = await SetState("false", Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
        using var csrf = await SetState("false", csrf: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var get = await Http.GetAsync($"/DistributionChannels/SetCouponEnabled/{_coupon}?channelId={_channel}&isEnabled=false");
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.IsTrue((await db.Coupons.FindAsync(_coupon))!.IsEnabled);
    }

    [TestMethod]
    public async Task MarketingReadDoesNotGrantCouponStateChanges()
    {
        await Seed();
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await manager.FindByEmailAsync(email))!;
            await manager.AddClaimAsync(user, new Claim(AppPermissions.Type, AppPermissionNames.CanReadDistributionChannels));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var html = await Http.GetStringAsync($"/DistributionChannels/Details/{_channel}");
        Assert.DoesNotContain("action=\"/DistributionChannels/SetCouponEnabled", html);
        using var denied = await SetState("false");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        using var scope2 = Server!.Services.CreateScope();
        Assert.IsTrue((await scope2.ServiceProvider.GetRequiredService<WeChatExamDbContext>().Coupons.FindAsync(_coupon))!.IsEnabled);
    }
}
