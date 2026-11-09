using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Server-only credentials for virtual order queries and delivery. Uses ordinary
/// stable-token requests; never forcibly invalidates another caller's token.
/// Disabling new sales must not prevent reconciliation of existing purchases.
/// </summary>
public sealed class VirtualPaymentAccessTokenService(
    IOptions<AppSettings> options, IHttpClientFactory httpClientFactory, IMemoryCache cache)
    : ISingletonDependency, IDisposable
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _cacheKey = new();

    public async Task<string> GetAsync(string appId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value.WeChat;
        if (string.IsNullOrWhiteSpace(appId) || appId != settings.AppId ||
            string.IsNullOrWhiteSpace(settings.AppSecret))
            throw new InvalidOperationException("Virtual payment access-token configuration is incomplete or mismatched.");

        if (cache.TryGetValue<string>(_cacheKey, out var existing)) return existing!;
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(_cacheKey, out existing)) return existing!;
            using var client = new TokenClient(new WechatApiClientOptions
            {
                AppId = settings.AppId, AppSecret = settings.AppSecret
            }, httpClientFactory.CreateClient(nameof(VirtualPaymentAccessTokenService)));
            CgibinStableTokenResponse response;
            // Measure lifetime from before the request, not after a slow response.
            var requestedAt = DateTimeOffset.UtcNow;
            try
            {
                response = await client.ExecuteCgibinStableTokenAsync(new CgibinStableTokenRequest
                {
                    ForceRefresh = false
                }, cancellationToken);
            }
            catch (CommonException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (CommonException)
            {
                // SDK exceptions can include requests or responses containing credentials.
                throw new InvalidOperationException("Virtual payment access-token request failed.");
            }

            if (!response.IsSuccessful())
                throw new InvalidOperationException($"Virtual payment access-token request failed (code {response.ErrorCode}).");
            if (string.IsNullOrWhiteSpace(response.AccessToken) || response.ExpiresIn <= 0)
                throw new InvalidOperationException("Virtual payment access-token response is invalid.");

            // expires_in is the remaining lifetime, including on repeated requests.
            // Leave a margin for delivery; a very short-lived token is not cached.
            var lifetime = Math.Min(response.ExpiresIn, 7200);
            if (requestedAt.AddSeconds(lifetime) <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("Virtual payment access-token response has expired.");
            var margin = Math.Clamp(lifetime / 10, 1, 60);
            var expiresAt = requestedAt.AddSeconds(lifetime - margin);
            if (expiresAt > DateTimeOffset.UtcNow)
                cache.Set(_cacheKey, response.AccessToken, expiresAt);
            return response.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Remove only the rejected cached credential. Serialize with acquisition so
    /// a delayed rejection cannot evict a newer token; never force-refresh WeChat.
    /// </summary>
    public async Task ForgetRejectedAsync(string appId, string rejectedToken, CancellationToken cancellationToken = default)
    {
        if (appId != options.Value.WeChat.AppId)
            throw new InvalidOperationException("Virtual payment access-token app is mismatched.");
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue<string>(_cacheKey, out var current) && current == rejectedToken)
                cache.Remove(_cacheKey);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void Dispose() => _refreshLock.Dispose();

    private sealed class TokenClient(WechatApiClientOptions settings, HttpClient httpClient)
        : WechatApiClient(settings, httpClient);
}
