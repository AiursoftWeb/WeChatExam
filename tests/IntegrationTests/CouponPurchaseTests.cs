using System.Net;
using System.Security.Claims;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class CouponPurchaseTests : TestBase
{
    private readonly Guid _couponId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();

    private async Task SeedOrders(bool singleUse = false, int extraOrders = 0)
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var channel = new DistributionChannel { Id = _channelId, AgencyName = "Test agency" };
        var coupon = new Coupon { Id = _couponId, Code = Guid.NewGuid().ToString("N"), DistributionChannel = channel, AmountInFen = 99999, IsSingleUse = singleUse };
        var student = new User { DisplayName = "Student <One>", UserName = Guid.NewGuid().ToString("N") };
        db.AddRange(channel, coupon, student);
        var at = new DateTime(2026, 11, 5, 15, 59, 59, DateTimeKind.Utc);
        PaymentOrder Order(string title, int amount, int discount, PaymentOrderStatus status, DateTime? paidAt) => new()
        {
            User = student, UserId = student.Id, CouponId = _couponId,
            OutTradeNo = Guid.NewGuid().ToString("N"), Description = title,
            AmountInFen = amount, DiscountInFen = discount, Status = status, PaidAt = paidAt
        };
        db.PaymentOrders.AddRange(
            Order("Historical course purchase", 15000, 5000, PaymentOrderStatus.Paid, at),
            Order("Fully discounted purchase", 0, 10000, PaymentOrderStatus.Paid, at.AddSeconds(1)),
            Order("Unpaid purchase", 100, 100, PaymentOrderStatus.Pending, null),
            Order("Refunded purchase", 100, 100, PaymentOrderStatus.Refunded, at));
        // A referred customer's purchase without this coupon must not enter its ledger.
        db.UserDistributionChannels.Add(new UserDistributionChannel { UserId = student.Id, DistributionChannelId = _channelId });
        db.PaymentOrders.Add(new PaymentOrder
        {
            UserId = student.Id, Description = "Without this coupon", OutTradeNo = Guid.NewGuid().ToString("N"),
            AmountInFen = 99900, Status = PaymentOrderStatus.Paid, PaidAt = at
        });
        for (var i = 0; i < extraOrders; i++)
            db.PaymentOrders.Add(Order($"Matching extra {i:00}", 100, 100, PaymentOrderStatus.Paid, at));
        await db.SaveChangesAsync();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SingleAndMultiUseCouponsShowActualCompletedPurchases(bool singleUse)
    {
        await SeedOrders(singleUse);
        await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}"));
        Assert.Contains("Historical course purchase", html);
        Assert.Contains("Fully discounted purchase", html);
        Assert.Contains("¥200.00", html);
        Assert.Contains("¥150.00", html);
        Assert.Contains("¥0.00", html);
        Assert.Contains("data-utc-time=", html);
        Assert.Contains("15:59:59\"", html);
        Assert.Contains("16:00:00\"", html);
        Assert.DoesNotContain("Unpaid purchase", html);
        Assert.DoesNotContain("Refunded purchase", html);
        Assert.DoesNotContain("Without this coupon", html);
        Assert.DoesNotContain("¥999.99", html);
        Assert.Contains("Student &lt;One&gt;", await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}"));
        Assert.Contains("/PaymentOrders/Details/", html);
        Assert.Contains("/PaymentOrders/UserPayments?userId=", html);
        var channel = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/Details/{_channelId}"));
        Assert.Contains("2 completed purchases", channel);
        Assert.Contains($"/DistributionChannels/CouponDetails/{_couponId}", channel);
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(99999, (await db.Coupons.FindAsync(_couponId))!.AmountInFen);
        Assert.AreEqual(4, await db.PaymentOrders.CountAsync(o => o.CouponId == _couponId));
    }

    [TestMethod]
    public async Task BeijingDatesSearchAndPagingApplyToRowsAndTotals()
    {
        await SeedOrders(extraOrders: 27);
        await LoginAsAdmin();
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?from=2026-11-05&to=2026-11-05&search=Historical"));
        Assert.Contains("Historical course purchase", html);
        Assert.DoesNotContain("Fully discounted purchase", html);
        Assert.DoesNotContain("Matching extra", html);
        Assert.Contains("¥150.00", html);
        html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?from=2026-11-06&to=2026-11-06"));
        Assert.Contains("Fully discounted purchase", html);
        Assert.DoesNotContain("Historical course purchase", html);
        html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?from=2026-11-05&to=2026-11-05&timeZone=UTC&search=purchase"));
        Assert.Contains("Fully discounted purchase", html);
        Assert.Contains("Historical course purchase", html);
        html = WebUtility.HtmlDecode(await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?search=Matching&page=99999"));
        Assert.Contains("2 / 2", html);
        Assert.AreEqual(2, System.Text.RegularExpressions.Regex.Matches(html, "Matching extra [0-9]{2}").Count);
        Assert.Contains("¥27.00", html);
        Assert.Contains("search=Matching", html);
        html = await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?search=missing");
        Assert.Contains("No completed purchases match these filters.", html);
    }

    [TestMethod]
    public async Task InvalidDatesAndUnknownCouponDoNotProduceSalesOrServerErrors()
    {
        await SeedOrders();
        await LoginAsAdmin();
        foreach (var filter in new[] { "from=2026-11-06&to=2026-11-05", "from=0001-01-01", "from=invalid", "from=2026-11-05&timeZone=missing-zone" })
        {
            var html = await Http.GetStringAsync($"/DistributionChannels/CouponDetails/{_couponId}?{filter}");
            Assert.DoesNotContain("Historical course purchase", html);
            Assert.Contains("validation-summary-errors", html);
        }
        Assert.AreEqual(HttpStatusCode.NotFound, (await Http.GetAsync($"/DistributionChannels/CouponDetails/{Guid.NewGuid()}")).StatusCode);
    }

    [TestMethod]
    public async Task MarketingReadAloneDoesNotGrantAccessToPurchaseDetails()
    {
        await SeedOrders();
        Assert.AreEqual(HttpStatusCode.Found, (await Http.GetAsync($"/DistributionChannels/CouponDetails/{_couponId}")).StatusCode);
        var (email, password) = await RegisterAndLoginAsync();
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = (await manager.FindByEmailAsync(email))!;
            await manager.AddClaimAsync(user, new Claim(AppPermissions.Type, AppPermissionNames.CanReadDistributionChannels));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var denied = await Http.GetAsync($"/DistributionChannels/CouponDetails/{_couponId}");
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        var channel = await Http.GetStringAsync($"/DistributionChannels/Details/{_channelId}");
        Assert.DoesNotContain($"/DistributionChannels/CouponDetails/{_couponId}", channel);
    }
}
