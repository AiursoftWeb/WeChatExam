using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.InMemory;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class CouponServiceTests
{
    private WeChatExamDbContext _dbContext = null!;
    private CouponService _service = null!;
    private Guid _channelId;
    private Guid _productId;

    [TestInitialize]
    public void TestInitialize()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new InMemoryContext(options);
        _service = new CouponService(_dbContext);

        // Setup base data
        var channel = new DistributionChannel { AgencyName = "Test Agency" };
        _dbContext.DistributionChannels.Add(channel);
        _channelId = channel.Id;

        var product = new VipProduct { Name = "Test VIP", PriceInFen = 10000, DurationDays = 30 };
        _dbContext.VipProducts.Add(product);
        _productId = product.Id;

        _dbContext.SaveChanges();
    }

    [TestMethod]
    public async Task TestCreateCoupon()
    {
        var coupon = await _service.CreateAsync(_channelId, "SAVE50", 5000, false);
        Assert.IsNotNull(coupon);
        Assert.AreEqual("SAVE50", coupon.Code);
        Assert.AreEqual(5000, coupon.AmountInFen);
        Assert.IsFalse(coupon.IsSingleUse);
    }

    [TestMethod]
    public async Task TestValidateCoupon_Valid()
    {
        await _service.CreateAsync(_channelId, "WELCOME", 1000, false);
        var (isValid, _, coupon) = await _service.ValidateCouponAsync("WELCOME", _productId, "user1");
        Assert.IsTrue(isValid);
        Assert.IsNotNull(coupon);
    }

    [TestMethod]
    public async Task TestValidateCoupon_InvalidCode()
    {
        var (isValid, error, _) = await _service.ValidateCouponAsync("NONEXISTENT", _productId, "user1");
        Assert.IsFalse(isValid);
        Assert.AreEqual("优惠码无效", error);
    }

    [TestMethod]
    public async Task TestClaimCoupon_AndAutoBind()
    {
        var userId = "new-user";
        await _service.CreateAsync(_channelId, "BINDME", 0, false);

        var (success, _) = await _service.ClaimCouponAsync(userId, "BINDME");
        Assert.IsTrue(success);

        // Verify binding
        var binding = await _dbContext.UserDistributionChannels.FirstOrDefaultAsync(b => b.UserId == userId);
        Assert.IsNotNull(binding);
        Assert.AreEqual(_channelId, binding.DistributionChannelId);

        // Verify claim record
        var claim = await _dbContext.UserClaimedCoupons.FirstOrDefaultAsync(c => c.UserId == userId);
        Assert.IsNotNull(claim);
        Assert.IsFalse(claim.IsUsed);
    }

    [TestMethod]
    public async Task TestClaimCoupon_SingleUseRestriction()
    {
        await _service.CreateAsync(_channelId, "SINGLE", 1000, true);

        // User 1 claims
        var (res1, _) = await _service.ClaimCouponAsync("user1", "SINGLE");
        Assert.IsTrue(res1);

        // User 2 attempts to claim the same single-use coupon
        var (res2, error) = await _service.ClaimCouponAsync("user2", "SINGLE");
        Assert.IsFalse(res2);
        Assert.AreEqual("该一次性优惠券已被他人领取", error);
    }

    [TestMethod]
    public async Task TestGetBestApplicableCoupon()
    {
        var userId = "user1";
        // Create two coupons: one for 10 yuan, one for 20 yuan
        await _service.CreateAsync(_channelId, "LOW", 1000, false);
        await _service.CreateAsync(_channelId, "HIGH", 2000, false);

        await _service.ClaimCouponAsync(userId, "LOW");
        await _service.ClaimCouponAsync(userId, "HIGH");

        var best = await _service.GetBestApplicableCouponAsync(userId, _productId);
        Assert.IsNotNull(best);
        Assert.AreEqual(2000, best.AmountInFen); // Should pick the 20 yuan one
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task DirectRedemptionRespectsExistingSingleUseClaim(bool singleUse)
    {
        var coupon = await _service.CreateAsync(_channelId, "CLAIMED", 1000, singleUse);
        // Unclaimed codes remain redeemable directly; only existing private claims restrict the user.
        Assert.IsTrue((await _service.ValidateCouponAsync(coupon.Code, _productId, "other-user")).IsValid);
        Assert.IsTrue((await _service.ClaimCouponAsync("owner", coupon.Code)).Success);
        _dbContext.ChangeTracker.Clear();

        var owner = await _service.ValidateCouponAsync(coupon.Code, _productId, "owner");
        var other = await _service.ValidateCouponAsync(coupon.Code, _productId, "other-user");
        Assert.IsTrue(owner.IsValid);
        Assert.AreEqual(!singleUse, other.IsValid);
        if (singleUse) Assert.AreEqual("该一次性优惠券已被他人领取", other.ErrorMessage);
        Assert.AreEqual(1, await _dbContext.UserClaimedCoupons.CountAsync());
        Assert.AreEqual("owner", (await _dbContext.UserClaimedCoupons.SingleAsync()).UserId);
        Assert.IsFalse((await _dbContext.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task TestRecordUsage()
    {
        var userId = "user1";
        var coupon = await _service.CreateAsync(_channelId, "USEME", 500, false);
        await _service.ClaimCouponAsync(userId, "USEME");

        var orderId = Guid.NewGuid();
        await _service.RecordUsageAsync(coupon.Id, userId, orderId, 500);

        // Verify claim is marked as used
        var claim = await _dbContext.UserClaimedCoupons.FirstAsync(c => c.UserId == userId && c.CouponId == coupon.Id);
        Assert.IsTrue(claim.IsUsed);

        // Verify usage record
        var usage = await _dbContext.CouponUsages.AnyAsync(u => u.PaymentOrderId == orderId);
        Assert.IsTrue(usage);
    }

    [TestMethod]
    [DataRow("channel")]
    [DataRow("coupon")]
    [DataRow("consumed")]
    public async Task UnavailableClaimedCouponCannotBeSelectedOrListed(string reason)
    {
        var coupon = await _service.CreateAsync(_channelId, "UNAVAILABLE", 5000, true);
        await _service.ClaimCouponAsync("user1", coupon.Code);
        if (reason == "channel")
            (await _dbContext.DistributionChannels.FindAsync(_channelId))!.IsEnabled = false;
        else if (reason == "coupon")
            coupon.IsEnabled = false;
        else
            coupon.UsedByUserId = "another-user";
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        var validation = await _service.ValidateCouponAsync(coupon.Code, _productId, "user1");
        Assert.IsFalse(validation.IsValid);
        Assert.IsNull(await _service.GetBestApplicableCouponAsync("user1", _productId));
        Assert.AreEqual(0, (await _service.GetMyAvailableCouponsAsync("user1")).Count);
        Assert.IsFalse((await _dbContext.UserClaimedCoupons.SingleAsync()).IsUsed);
        Assert.AreEqual(0, await _dbContext.CouponUsages.CountAsync());
    }

    [TestMethod]
    public async Task DisabledChannelPreventsClaimWithoutBindingUserAndCanBeReenabled()
    {
        var coupon = await _service.CreateAsync(_channelId, "CHANNEL", 1000, false);
        var channel = (await _dbContext.DistributionChannels.FindAsync(_channelId))!;
        channel.IsEnabled = false;
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        Assert.IsFalse((await _service.ClaimCouponAsync("user1", coupon.Code)).Success);
        Assert.AreEqual(0, await _dbContext.UserClaimedCoupons.CountAsync());
        Assert.AreEqual(0, await _dbContext.UserDistributionChannels.CountAsync());

        (await _dbContext.DistributionChannels.FindAsync(_channelId))!.IsEnabled = true;
        await _dbContext.SaveChangesAsync();
        Assert.IsTrue((await _service.ClaimCouponAsync("user1", coupon.Code)).Success);
        Assert.AreEqual(coupon.Id, (await _service.GetBestApplicableCouponAsync("user1", _productId))!.Id);
    }

    [TestMethod]
    public async Task AutomaticSelectionKeepsBestEligibleProductDiscount()
    {
        var otherChannel = new DistributionChannel { AgencyName = "Other channel" };
        var otherProduct = new VipProduct { Name = "Other school", PriceInFen = 10000, DurationDays = 30 };
        _dbContext.AddRange(otherChannel, otherProduct);
        await _dbContext.SaveChangesAsync();
        var disabled = await _service.CreateAsync(_channelId, "DISABLED", 5000, false);
        var wrongProduct = await _service.CreateAsync(otherChannel.Id, "OTHER", 4000, false, [otherProduct.Id]);
        var best = await _service.CreateAsync(otherChannel.Id, "BEST", 2000, false, [_productId]);
        var lower = await _service.CreateAsync(otherChannel.Id, "LOWER", 1000, false);
        foreach (var coupon in new[] { disabled, wrongProduct, best, lower })
            await _service.ClaimCouponAsync("user1", coupon.Code);
        (await _dbContext.DistributionChannels.FindAsync(_channelId))!.IsEnabled = false;
        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();

        Assert.AreEqual(best.Id, (await _service.GetBestApplicableCouponAsync("user1", _productId))!.Id);
        Assert.AreEqual(wrongProduct.Id, (await _service.GetBestApplicableCouponAsync("user1", otherProduct.Id))!.Id);
        CollectionAssert.AreEquivalent(new[] { wrongProduct.Id, best.Id, lower.Id },
            (await _service.GetMyAvailableCouponsAsync("user1")).Select(c => c.Id).ToArray());
        Assert.IsNull(await _service.GetBestApplicableCouponAsync("unrelated-user", _productId));
    }

    [TestMethod]
    public async Task TestCreateCoupon_NegativeDiscount()
    {
        try
        {
            await _service.CreateAsync(_channelId, "NEGATIVE", -100, false);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Success
        }
    }

    [TestMethod]
    public async Task TestCreateCoupon_DiscountTooHigh()
    {
        // Product is 10000 fen (100 CNY), try to create a 20000 fen coupon
        try
        {
            await _service.CreateAsync(_channelId, "TOO_MUCH", 20000, false);
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Success
        }
    }

    [TestMethod]
    public async Task TestCreateCoupon_TargetedDiscountTooHigh()
    {
        var product = new VipProduct { Name = "Cheap VIP", PriceInFen = 500, IsEnabled = true };
        _dbContext.VipProducts.Add(product);
        await _dbContext.SaveChangesAsync();

        // Try to create 1000 fen coupon for 500 fen product
        try
        {
            await _service.CreateAsync(_channelId, "TARGET_TOO_MUCH", 1000, false, new List<Guid> { product.Id });
            Assert.Fail("Should have thrown InvalidOperationException");
        }
        catch (InvalidOperationException)
        {
            // Success
        }
    }
}
