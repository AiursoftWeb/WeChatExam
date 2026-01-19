using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Services;

public class DistributionChannelService(WeChatExamDbContext context) : IDistributionChannelService
{
    // Base32 characters for generating readable short codes (excludes confusing chars like 0, O, I, L)
    private const string Base32Chars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public async Task<DistributionChannel> CreateAsync(string agencyName)
    {
        var code = await GenerateUniqueCodeAsync();
        var channel = new DistributionChannel
        {
            Id = Guid.NewGuid(),
            Code = code,
            AgencyName = agencyName,
            IsEnabled = true
        };

        context.DistributionChannels.Add(channel);
        await context.SaveChangesAsync();

        return channel;
    }

    public async Task<List<DistributionChannel>> GetAllAsync()
    {
        return await context.DistributionChannels
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<DistributionChannel?> GetByIdAsync(Guid id)
    {
        return await context.DistributionChannels.FindAsync(id);
    }

    public async Task<DistributionChannel?> GetByCodeAsync(string code)
    {
        return await context.DistributionChannels
            .FirstOrDefaultAsync(c => c.Code == code);
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

    public async Task<bool> BindUserAsync(string userId, string code)
    {
        // Check if user already has a binding
        var existingBinding = await context.UserDistributionChannels
            .FirstOrDefaultAsync(b => b.UserId == userId);
        
        if (existingBinding != null)
        {
            // User already bound to a channel, don't rebind
            return false;
        }

        // Find the channel by code
        var channel = await context.DistributionChannels
            .FirstOrDefaultAsync(c => c.Code == code);

        if (channel == null)
        {
            // Channel not found
            return false;
        }

        if (!channel.IsEnabled)
        {
            // Channel is disabled, don't accept new bindings
            return false;
        }

        // Create the binding
        var binding = new UserDistributionChannel
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DistributionChannelId = channel.Id
        };

        context.UserDistributionChannels.Add(binding);
        await context.SaveChangesAsync();

        return true;
    }

    public async Task<ChannelStats> GetStatsAsync(Guid channelId)
    {
        var registrationCount = await context.UserDistributionChannels
            .CountAsync(b => b.DistributionChannelId == channelId);

        // Payment statistics are not implemented yet (Order/Payment tables don't exist)
        // When implemented, query the orders table for users bound to this channel
        return new ChannelStats
        {
            RegistrationCount = registrationCount,
            PaidOrderCount = 0,
            TotalPaidAmount = 0
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

    /// <summary>
    /// Generate a unique 8-character Base32 code
    /// </summary>
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
        while (await context.DistributionChannels.AnyAsync(c => c.Code == code));

        return code;
    }
}
