using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class DistributionChannelService(WeChatExamDbContext context) : IDistributionChannelService
{
    // Base32 characters for generating readable short codes (excludes confusing chars like 0, O, I, L)
    private const string Base32Chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public async Task<DistributionChannel> CreateAsync(string agencyName)
    {
        var channel = new DistributionChannel
        {
            Id = Guid.NewGuid(),
            AgencyName = agencyName,
            IsEnabled = true
        };

        context.DistributionChannels.Add(channel);
        
        // Create a default public coupon for this channel
        var code = await GenerateUniqueCodeAsync();
        var coupon = new Coupon
        {
            DistributionChannelId = channel.Id,
            Code = code,
            AmountInFen = 0, // Default no discount
            IsSingleUse = false,
            IsEnabled = true
        };
        context.Coupons.Add(coupon);

        await context.SaveChangesAsync();
        return channel;
    }

    public async Task<List<DistributionChannel>> GetAllAsync()
    {
        return await context.DistributionChannels
            .Include(c => c.Coupons)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<DistributionChannel?> GetByIdAsync(Guid id)
    {
        return await context.DistributionChannels
            .Include(c => c.Coupons)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task SetEnabledAsync(Guid id, bool isEnabled)
    {
        var channel = await context.DistributionChannels.FindAsync(id);
        if (channel != null)
        {
            channel.IsEnabled = isEnabled;
            await context.SaveChangesAsync();
        }
    }

    public async Task<bool> BindUserByCouponCodeAsync(string userId, string code)
    {
        // Check if user already has a binding
        var existingBinding = await context.UserDistributionChannels
            .FirstOrDefaultAsync(b => b.UserId == userId);
        
        if (existingBinding != null)
        {
            return false;
        }

        // Find the coupon by code
        var coupon = await context.Coupons
            .Include(c => c.DistributionChannel)
            .FirstOrDefaultAsync(c => c.Code == code.ToUpper());

        if (coupon == null || coupon.DistributionChannel == null)
        {
            return false;
        }

        if (!coupon.IsEnabled || !coupon.DistributionChannel.IsEnabled)
        {
            return false;
        }

        // Create the binding
        var binding = new UserDistributionChannel
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DistributionChannelId = coupon.DistributionChannelId
        };

        context.UserDistributionChannels.Add(binding);
        await context.SaveChangesAsync();

        return true;
    }

    public async Task<ChannelStats> GetStatsAsync(Guid channelId)
    {
        // 1. Get all users bound to this channel
        var boundUserIds = await context.UserDistributionChannels
            .Where(b => b.DistributionChannelId == channelId)
            .Select(b => b.UserId)
            .ToListAsync();

        // 2. Calculate stats for orders from these bound users (Organic Attribution)
        var boundUserPaidOrders = await context.PaymentOrders
            .Where(o => o.Status == PaymentOrderStatus.Paid && boundUserIds.Contains(o.UserId))
            .ToListAsync();

        // 3. Calculate stats for orders that explicitly used a coupon from this channel (Direct Attribution)
        // Note: Some users might not be bound but used a coupon, or bound users might use a coupon from the same channel.
        var couponPaidOrders = await context.PaymentOrders
            .Include(o => o.Coupon)
            .Where(o => o.Status == PaymentOrderStatus.Paid && 
                        o.Coupon != null && 
                        o.Coupon.DistributionChannelId == channelId)
            .ToListAsync();

        // Combine unique orders to avoid double counting if a bound user used a coupon
        var allAttributedOrders = boundUserPaidOrders
            .UnionBy(couponPaidOrders, o => o.Id)
            .ToList();

        return new ChannelStats
        {
            RegistrationCount = boundUserIds.Count,
            PaidOrderCount = allAttributedOrders.Count,
            TotalPaidAmount = allAttributedOrders.Sum(o => o.AmountInFen) / 100m
        };
    }

    public async Task DeleteAsync(Guid id)
    {
        var channel = await context.DistributionChannels.FindAsync(id);
        if (channel != null)
        {
            context.DistributionChannels.Remove(channel);
            await context.SaveChangesAsync();
        }
    }

    private async Task<string> GenerateUniqueCodeAsync()
    {
        const int codeLength = 8;
        var random = new Random();
        string code;

        do
        {
            var chars = new char[codeLength];
            for (int i = 0; i < codeLength; i++)
            {
                chars[i] = Base32Chars[random.Next(Base32Chars.Length)];
            }
            code = new string(chars);
        }
        while (await context.Coupons.AnyAsync(c => c.Code == code));

        return code;
    }
}
