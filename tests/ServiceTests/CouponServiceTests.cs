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
