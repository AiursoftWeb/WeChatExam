using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Aiursoft.WeChatExam.Authorization;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class PaymentRefundReviewTests : TestBase
{
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly string _buyerId = Guid.NewGuid().ToString();

    private async Task Seed(int count = 1, string reason = "membership_changed_or_missing", bool malformed = false,
        PaymentChannel channel = PaymentChannel.WeChatVirtual, string eventType = "VirtualPaymentRefundReview")
    {
        using var scope = Server!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        var user = new User { Id = _buyerId, UserName = "refund-" + _buyerId, DisplayName = "Refund buyer" };
        var product = new VipProduct { Id = _productId, Name = "Refund course", PriceInFen = 6800, DurationDays = 30 };
        var order = new PaymentOrder { Id = _orderId, User = user, UserId = user.Id, VipProduct = product,
            OutTradeNo = "REVIEW-" + _orderId.ToString("N")[..20], AmountInFen = 6800,
            Description = "Review test", Channel = channel, Status = PaymentOrderStatus.Refunded,
            PaidAt = DateTime.UtcNow.AddHours(-1) };
        db.AddRange(order, new VipMembership { User = user, UserId = user.Id, VipProduct = product,
            StartTime = DateTime.UtcNow.AddDays(-1), EndTime = DateTime.UtcNow.AddDays(30), LastPaymentOrderId = order.Id });
        for (var i = 0; i < count; i++)
            db.PaymentLogs.Add(new PaymentLog { PaymentOrder = order, PaymentOrderId = order.Id,
                EventType = eventType, CreatedAt = DateTime.UtcNow.AddSeconds(i),
                RawData = malformed ? "{invalid-audit" : JsonSerializer.Serialize(new
                {
                    confirmation = new VirtualPaymentRefundConfirmation(order.Id, "platform-order", $"refund-{i:D2}", 6800), reason
                }) });
        await db.SaveChangesAsync();
    }

    [TestCleanup]
    public override async Task CleanServer()
    {
        if (Server != null)
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            db.PaymentLogs.RemoveRange(await db.PaymentLogs.Where(l => l.PaymentOrderId == _orderId).ToListAsync());
            db.VipMemberships.RemoveRange(await db.VipMemberships.Where(v => v.UserId == _buyerId).ToListAsync());
            db.PaymentOrders.RemoveRange(await db.PaymentOrders.Where(o => o.Id == _orderId).ToListAsync());
            db.VipProducts.RemoveRange(await db.VipProducts.Where(p => p.Id == _productId).ToListAsync());
            db.Users.RemoveRange(await db.Users.Where(u => u.Id == _buyerId).ToListAsync());
            await db.SaveChangesAsync();
        }
        await base.CleanServer();
    }

    private string ReviewUrl(int page = 1) => $"/PaymentOrders/RefundReviews?orderId={_orderId}&page={page}";

    [TestMethod]
    public async Task ReviewsShowReadableReasonAmountsTimesAndAuthorizedNavigationWithoutWriting()
    {
        await Seed(); await LoginAsAdmin();
        DateTime end;
        int logs;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            end = (await db.VipMemberships.SingleAsync(v => v.UserId == _buyerId)).EndTime;
            logs = await db.PaymentLogs.CountAsync(l => l.PaymentOrderId == _orderId);
        }
        var html = WebUtility.HtmlDecode(await Http.GetStringAsync(ReviewUrl()));
        Assert.Contains("Refund course", html);
        Assert.Contains("Refund buyer", html);
        Assert.Contains("refund-00", html);
        Assert.Contains("68.00", html);
        Assert.Contains("Automatic membership removal stopped", html);
        Assert.Contains("data-utc-time=", html);
        Assert.Contains("These records remain after manual changes", html);
        Assert.Contains($"/PaymentOrders/Details/{_orderId}", html);
        Assert.Contains($"/PaymentOrders/UserPayments?userId={_buyerId}", html);
        Assert.Contains($"/VipMembers?userId={_buyerId}", html);
        Assert.Contains("/PaymentOrders/RefundReviews", await Http.GetStringAsync("/PaymentOrders"));
        Assert.Contains("/PaymentOrders/RefundReviews?orderId=", await Http.GetStringAsync($"/PaymentOrders/Details/{_orderId}"));
        using var final = Server!.Services.CreateScope();
        var saved = final.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(end, (await saved.VipMemberships.SingleAsync(v => v.UserId == _buyerId)).EndTime);
        Assert.AreEqual(logs, await saved.PaymentLogs.CountAsync(l => l.PaymentOrderId == _orderId));
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await saved.PaymentOrders.FindAsync(_orderId))!.Status);
    }

    [TestMethod]
    public async Task ReviewRecordsArePaginatedAndRetainOrderFilter()
    {
        await Seed(25); await LoginAsAdmin();
        var first = WebUtility.HtmlDecode(await Http.GetStringAsync(ReviewUrl()));
        var second = WebUtility.HtmlDecode(await Http.GetStringAsync(ReviewUrl(2)));
        Assert.Contains("refund-24", first);
        Assert.DoesNotContain("refund-04", first);
        Assert.Contains("refund-04", second);
        Assert.DoesNotContain("refund-24", second);
        Assert.Contains($"orderId={_orderId}&page=2", first);
        Assert.Contains($"orderId={_orderId}&page=1", second);
        Assert.Contains("No refund reconciliation records", await Http.GetStringAsync($"/PaymentOrders/RefundReviews?orderId={Guid.NewGuid()}"));
        foreach (var suffix in new[] { "page=0", "page=-1", "page=2147483647", "orderId=not-a-guid" })
        {
            using var response = await Http.GetAsync("/PaymentOrders/RefundReviews?" + suffix);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [TestMethod]
    [DataRow("partial_or_adjusted_refund")]
    [DataRow("entitlement_evidence_missing_or_invalid")]
    [DataRow("previous_entitlement_purchase_unavailable")]
    [DataRow("payment_not_locally_settled")]
    [DataRow("original_payment_evidence_missing")]
    [DataRow("unknown-reason")]
    public async Task ReasonsUseBusinessExplanationsInsteadOfRawProtocolData(string reason)
    {
        await Seed(reason: reason); await LoginAsAdmin();
        var html = await Http.GetStringAsync(ReviewUrl());
        Assert.DoesNotContain(reason, html);
        Assert.DoesNotContain("&quot;confirmation&quot;", html);
        Assert.Contains("Refund course", html);
        Assert.Contains("refund-00", html);
        if (reason == "partial_or_adjusted_refund") Assert.Contains("refund amount differs", html);
        if (reason == "unknown-reason") Assert.Contains("technical support", html);
    }

    [TestMethod]
    public async Task UnreadableAuditRecordRemainsVisibleWithoutFabricatingAmount()
    {
        await Seed(malformed: true); await LoginAsAdmin();
        var html = await Http.GetStringAsync(ReviewUrl());
        Assert.Contains("technical support", html);
        Assert.Contains("Unavailable", html);
        Assert.DoesNotContain("68.00", html);
        Assert.DoesNotContain("invalid-audit", html);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OrdinaryOrdersAndSuccessfulRefundsAreNotReportedAsManualReviews(bool ordinary)
    {
        await Seed(channel: ordinary ? PaymentChannel.WeChatJsApi : PaymentChannel.WeChatVirtual,
            eventType: ordinary ? "VirtualPaymentRefundReview" : "VirtualPaymentRefunded");
        await LoginAsAdmin();
        Assert.Contains("No refund reconciliation records", await Http.GetStringAsync(ReviewUrl()));
        Assert.DoesNotContain("View refund reconciliation records", await Http.GetStringAsync($"/PaymentOrders/Details/{_orderId}"));
    }

    [TestMethod]
    public async Task EarlyRefundActionRequiresPostCsrfAndValidOrderId()
    {
        await Seed(reason: "payment_not_locally_settled"); await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var order = await db.PaymentOrders.SingleAsync(o => o.Id == _orderId);
            order.Status = PaymentOrderStatus.Pending; order.PaidAt = null;
            await db.SaveChangesAsync();
        }
        var html = await Http.GetStringAsync(ReviewUrl());
        Assert.Contains("Verify refund and close review", html);
        Assert.Contains("does not request a refund", html);
        using var get = await Http.GetAsync($"/PaymentOrders/ReconcileRefund?id={_orderId}");
        Assert.AreEqual(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var noToken = await Http.PostAsync("/PaymentOrders/ReconcileRefund", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = _orderId.ToString() }));
        Assert.AreEqual(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var badId = await PostForm("/PaymentOrders/ReconcileRefund", new Dictionary<string, string> { ["id"] = "invalid" }, ReviewUrl());
        Assert.AreEqual(HttpStatusCode.BadRequest, badId.StatusCode);
        using var check = Server!.Services.CreateScope();
        var saved = check.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(PaymentOrderStatus.Pending, (await saved.PaymentOrders.FindAsync(_orderId))!.Status);
        Assert.AreEqual(1, await saved.PaymentLogs.CountAsync(l => l.PaymentOrderId == _orderId));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OnlyAnExactlyLinkedSettlementChangesTheReviewBadge(bool wrongLink)
    {
        await Seed(reason: "payment_not_locally_settled"); await LoginAsAdmin();
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var order = await db.PaymentOrders.SingleAsync(o => o.Id == _orderId);
            order.PaidAt = null;
            var review = await db.PaymentLogs.SingleAsync(l => l.PaymentOrderId == _orderId);
            db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = _orderId, EventType = "VirtualPaymentRefunded",
                RawData = JsonSerializer.Serialize(new { reviewId = wrongLink ? Guid.NewGuid() : review.Id,
                    reason = "refunded_before_local_fulfillment",
                    confirmation = new VirtualPaymentRefundConfirmation(_orderId, "platform-order", "refund-00", 6800) }) });
            await db.SaveChangesAsync();
        }
        var html = await Http.GetStringAsync(ReviewUrl());
        Assert.AreEqual(!wrongLink, html.Contains("This order was refunded before membership activation"));
        Assert.DoesNotContain("Verify refund and close review", html);
        Assert.Contains("refund-00", html);
    }

    [TestMethod]
    public async Task ReviewAccessRequiresOrderReadPermissionAndMembershipManagementIsSeparate()
    {
        await Seed();
        using var anonymous = await Http.GetAsync(ReviewUrl());
        Assert.IsTrue(anonymous.StatusCode == HttpStatusCode.Found || anonymous.StatusCode == HttpStatusCode.Unauthorized);
        var (email, password) = await RegisterAndLoginAsync();
        using var denied = await Http.GetAsync(ReviewUrl());
        Assert.IsTrue(denied.StatusCode == HttpStatusCode.Forbidden || denied.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
        using (var scope = Server!.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await manager.AddClaimAsync((await manager.FindByEmailAsync(email))!, new Claim(AppPermissions.Type, AppPermissionNames.CanReadPaymentOrders));
        }
        await PostForm("/Account/Login", new Dictionary<string, string> { ["EmailOrUserName"] = email, ["Password"] = password });
        var html = await Http.GetStringAsync(ReviewUrl());
        Assert.Contains("View memberships and purchases", html);
        Assert.DoesNotContain("Manage memberships", html);
        Assert.DoesNotContain("Verify refund and close review", html);
        using var post = await Http.PostAsync("/PaymentOrders/ReconcileRefund", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = _orderId.ToString() }));
        Assert.IsTrue(post.StatusCode == HttpStatusCode.Forbidden || post.Headers.Location?.ToString().Contains("/Error/Unauthorized") == true);
    }
}
