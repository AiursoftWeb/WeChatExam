using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Configuration;

namespace Aiursoft.WeChatExam.Tests.TestServices;

public sealed class StubWeChatService : IWeChatService
{
    public Dictionary<string, WeChatSessionResult> Sessions { get; } = new();
    public WeChatSessionResult? DefaultSession { get; set; }
    public Task<WeChatSessionResult> CodeToSessionAsync(string code)
        => Task.FromResult(Sessions.TryGetValue(code, out var session) ? session : DefaultSession!);
}

public sealed class StubOllamaService : IOllamaService
{
    public string Response { get; set; } = string.Empty;
    public Exception? Failure { get; set; }
    public List<string> Questions { get; } = new();
    public Task<string> AskQuestion(string question, CancellationToken cancellationToken = default)
    {
        Questions.Add(question);
        return Failure == null ? Task.FromResult(Response) : Task.FromException<string>(Failure);
    }
}

public sealed class StubWeChatPayService : IWeChatPayService
{
    public List<VipMembership> Memberships { get; set; } = new();
    public int VipStatusCalls { get; private set; }
    public bool NotifyResult { get; set; }
    public Task<List<VipMembership>> GetVipStatusListAsync(string userId)
    {
        VipStatusCalls++;
        return Task.FromResult(Memberships);
    }
    public Task<bool> HandlePaymentNotifyAsync(string requestBody, string signature, string timestamp, string nonce, string serialNumber) => Task.FromResult(NotifyResult);
    public Task<CreateOrderResult> CreateOrderAsync(string userId, string openId, Guid vipProductId, string? couponCode = null) => Task.FromResult<CreateOrderResult>(null!);
    public Task<JsApiPayParams> GetJsApiPayParamsAsync(string prepayId) => Task.FromResult<JsApiPayParams>(null!);
    public Task<PaymentOrder?> QueryOrderStatusAsync(string outTradeNo, string? userId) => Task.FromResult<PaymentOrder?>(null);
    public Task<bool> HasVipForCategoryAsync(string userId, Guid categoryId) => Task.FromResult(false);
}

public sealed class StubDistributionChannelService : IDistributionChannelService
{
    public Task<DistributionChannel> CreateAsync(string agencyName) => Task.FromResult<DistributionChannel>(null!);
    public Task<List<DistributionChannel>> GetAllAsync() => Task.FromResult(new List<DistributionChannel>());
    public Task<DistributionChannel?> GetByIdAsync(Guid id) => Task.FromResult<DistributionChannel?>(null);
    public Task SetEnabledAsync(Guid id, bool isEnabled) => Task.CompletedTask;
    public Task<bool> BindUserByCouponCodeAsync(string userId, string code) => Task.FromResult(false);
    public Task<ChannelStats> GetStatsAsync(Guid channelId) => Task.FromResult<ChannelStats>(null!);
    public Task DeleteAsync(Guid id) => Task.CompletedTask;
}

public sealed class StubVipProductService : IVipProductService
{
    public Task<List<VipProduct>> GetAllAsync() => Task.FromResult(new List<VipProduct>());
    public Task<VipProduct?> GetByIdAsync(Guid id) => Task.FromResult<VipProduct?>(null);
    public Task<VipProduct> CreateAsync(string name, VipProductType type, Guid? categoryId, int priceInFen, int durationDays, Guid? institutionId = null) => Task.FromResult<VipProduct>(null!);
    public Task UpdateAsync(Guid id, string name, VipProductType type, Guid? categoryId, int priceInFen, int durationDays, bool isEnabled, Guid? institutionId = null) => Task.CompletedTask;
    public Task DeleteAsync(Guid id) => Task.CompletedTask;
    public Task<List<VipProduct>> GetEnabledAsync(Guid? categoryId = null, VipProductType? type = null, Guid? institutionId = null) => Task.FromResult(new List<VipProduct>());
    public Task<bool> HasPurchasesAsync(Guid id) => Task.FromResult(false);
}

public sealed class StubGlobalSettingsService : IGlobalSettingsService
{
    public string GradingPrompt { get; set; } = string.Empty;
    public bool EmbeddingEnabled { get; set; }
    public string EmbeddingEndpoint { get; set; } = "https://embedding.example.com";
    public Task<bool> IsAiSearchEnabledAsync() => Task.FromResult(EmbeddingEnabled);
    public Task<string> GetEmbeddingEndpointAsync() => Task.FromResult(EmbeddingEndpoint);
    public Task<string> GetEmbeddingTokenAsync() => Task.FromResult(string.Empty);
    public Task<string> GetSettingValueAsync(string key) => Task.FromResult(key switch
    {
        SettingsMap.AiPromptGradingDefault => GradingPrompt,
        SettingsMap.EmbeddingModel => "bge-m3:latest",
        _ => string.Empty
    });
    public Task<bool> GetBoolSettingAsync(string key) => Task.FromResult(false);
    public Task<int> GetIntSettingAsync(string key) => Task.FromResult(0);
    public bool IsOverriddenByConfig(string key) => false;
    public Task UpdateSettingAsync(string key, string value) => Task.CompletedTask;
    public Task SeedSettingsAsync() => Task.CompletedTask;
}

public sealed class StubGradingService : IGradingService
{
    public Func<string, string, GradingStrategy, int, string, Task<GradingResult>> Grade { get; set; } = null!;
    public int Calls { get; private set; }
    public Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore, string content)
    {
        Calls++;
        return Grade(userAnswer, standardAnswer, strategy, maxScore, content);
    }
    public Task<GradingResult> GradeAsync(Question question, string userAnswer) => throw new NotSupportedException();
    public Task<GradingResult> GradeAsync(string userAnswer, string standardAnswer, GradingStrategy strategy, int maxScore, string content, string explanation) => throw new NotSupportedException();
}
