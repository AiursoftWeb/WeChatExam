using System.Net;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CouponDeletionTests : TestBase
{
    [TestMethod]
    [DataRow("pending")]
    [DataRow("audit")]
    [DataRow("claimed")]
    [DataRow("legacy")]
    public async Task HistoryPreventsBothCouponAndChannelDeletionWithAnExplanation(string history)
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = await db.Users.FirstAsync();
        var channel = new DistributionChannel { AgencyName = "Protected channel" };
        var coupon = new Coupon { Code = Guid.NewGuid().ToString("N"), DistributionChannel = channel, AmountInFen = 100 };
        db.Add(coupon);
        switch (history)
        {
            case "pending": db.PaymentOrders.Add(new PaymentOrder { UserId = user.Id, Coupon = coupon, OutTradeNo = Guid.NewGuid().ToString("N"), Description = "Pending purchase", Status = PaymentOrderStatus.Pending }); break;
            case "audit": db.Changes.Add(new Change { TargetUserId = user.Id, Coupon = coupon, Type = ChangeType.VipActivatedViaCoupon }); break;
            case "claimed": db.UserClaimedCoupons.Add(new UserClaimedCoupon { UserId = user.Id, Coupon = coupon }); break;
            case "legacy": coupon.UsedByUserId = user.Id; break;
        }
        await db.SaveChangesAsync();
        var path = $"/DistributionChannels/DeleteCoupon/{coupon.Id}?channelId={channel.Id}";
        var html = await Http.GetStringAsync(path);
        Assert.Contains("Cannot delete:", html);
        Assert.Contains("disabled=", html);
        Assert.Contains("/PaymentOrders", html);
        Assert.Contains("/Accounting/History", html);
        Assert.Contains($"/DistributionChannels/CouponDetails/{coupon.Id}", html);
        using var rejected = await PostForm(path, new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("This coupon has history and cannot be deleted.", await rejected.Content.ReadAsStringAsync());
        using var channelRejected = await PostForm($"/DistributionChannels/Delete/{channel.Id}", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.OK, channelRejected.StatusCode);
        Assert.Contains("This channel has coupon history.", await channelRejected.Content.ReadAsStringAsync());
        db.ChangeTracker.Clear();
        Assert.IsTrue(await db.Coupons.AnyAsync(c => c.Id == coupon.Id));
        Assert.IsTrue(await db.DistributionChannels.AnyAsync(c => c.Id == channel.Id));
    }

    [TestMethod]
    public async Task UnusedCouponRequiresConfirmationAndCanBeDeletedOnlyFromItsOwnChannel()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var channel = new DistributionChannel { AgencyName = "Unused channel" };
        var coupon = new Coupon { Code = Guid.NewGuid().ToString("N"), DistributionChannel = channel, AmountInFen = 100 };
        db.Add(coupon); await db.SaveChangesAsync();
        var path = $"/DistributionChannels/DeleteCoupon/{coupon.Id}?channelId={channel.Id}";
        var html = await Http.GetStringAsync(path);
        Assert.Contains("No orders, claims or history block deletion.", html);
        Assert.Contains("Permanently Delete Coupon", html);
        using var mismatched = await PostForm($"/DistributionChannels/DeleteCoupon/{coupon.Id}?channelId={Guid.NewGuid()}", new Dictionary<string, string>(), tokenUrl: path);
        Assert.AreEqual(HttpStatusCode.NotFound, mismatched.StatusCode);
        using var csrf = await PostForm(path, new Dictionary<string, string>(), includeToken: false);
        Assert.AreEqual(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var deleted = await PostForm(path, new Dictionary<string, string>());
        AssertRedirect(deleted, $"/DistributionChannels/Details/{channel.Id}");
        db.ChangeTracker.Clear();
        Assert.IsFalse(await db.Coupons.AnyAsync(c => c.Id == coupon.Id));
        Assert.IsTrue(await db.DistributionChannels.AnyAsync(c => c.Id == channel.Id));
        using var deletedChannel = await PostForm($"/DistributionChannels/Delete/{channel.Id}", new Dictionary<string, string>());
        AssertRedirect(deletedChannel, "/DistributionChannels");
        Assert.IsFalse(await db.DistributionChannels.AnyAsync(c => c.Id == channel.Id));
    }

    [TestMethod]
    public async Task AClaimAfterConfirmationIsRecheckedBeforeDeletion()
    {
        await LoginAsAdmin();
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var channel = new DistributionChannel { AgencyName = "Claim race" };
        var coupon = new Coupon { Code = Guid.NewGuid().ToString("N"), DistributionChannel = channel, AmountInFen = 100 };
        db.Add(coupon); await db.SaveChangesAsync();
        var path = $"/DistributionChannels/DeleteCoupon/{coupon.Id}?channelId={channel.Id}";
        var token = await GetAntiCsrfToken(path);
        var user = await db.Users.FirstAsync();
        db.UserClaimedCoupons.Add(new UserClaimedCoupon { UserId = user.Id, Coupon = coupon });
        await db.SaveChangesAsync();
        using var response = await PostForm(path, new Dictionary<string, string> { ["__RequestVerificationToken"] = token });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("This coupon has history", await response.Content.ReadAsStringAsync());
        Assert.IsTrue(await db.UserClaimedCoupons.AnyAsync(c => c.CouponId == coupon.Id));
    }
}
