using System.Text.Json;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

public interface IVirtualPaymentGoodsGateway
{
    Task<GoodsTaskResult> QueryAsync(bool publish, CancellationToken token);
    Task StartAsync(VirtualPaymentGoods goods, bool publish, CancellationToken token);
}

public sealed record GoodsTaskItem(string Id, int Status, string? Name, int? Price, string? Remark);
public sealed record GoodsTaskResult(int Status, IReadOnlyList<GoodsTaskItem> Items);

// Uses the existing WeChat SDK and token cache. No new HTTP/signing framework.
public class VirtualPaymentGoodsGateway(IOptions<AppSettings> options, IHttpClientFactory factory,
    VirtualPaymentAccessTokenService tokens) : IVirtualPaymentGoodsGateway
{
    public async Task<GoodsTaskResult> QueryAsync(bool publish, CancellationToken token)
    {
        using var client = CreateClient();
        var access = await tokens.GetAsync(options.Value.WeChat.AppId, token);
        try
        {
            var env = options.Value.WeChat.VirtualPayment.Environment;
            WechatApiResponse response = publish
                ? await client.ExecuteXPayQueryPublishGoodsAsync(new XPayQueryPublishGoodsRequest { AccessToken = access, Environment = env }, token)
                : await client.ExecuteXPayQueryUploadGoodsAsync(new XPayQueryUploadGoodsRequest { AccessToken = access, Environment = env }, token);
            using var json = await ValidateAsync(response, access, token);
            var root = json.RootElement;
            if (!root.TryGetProperty("status", out var state) || !state.TryGetInt32(out var status) || status is < 0 or > 3)
                throw new InvalidOperationException("Invalid goods task response.");
            var items = new List<GoodsTaskItem>();
            if (root.TryGetProperty(publish ? "publish_item" : "upload_item", out var array))
                foreach (var item in array.EnumerateArray())
                    items.Add(new GoodsTaskItem(item.GetProperty("id").GetString()!,
                        item.GetProperty(publish ? "publish_status" : "upload_status").GetInt32(),
                        item.TryGetProperty("name", out var name) ? name.GetString() : null,
                        item.TryGetProperty("price", out var price) ? price.GetInt32() : null,
                        item.TryGetProperty("remark", out var remark) ? remark.GetString() : null));
            return new GoodsTaskResult(status, items);
        }
        catch (CommonException) { throw new InvalidOperationException("WeChat goods request failed."); }
    }

    public async Task StartAsync(VirtualPaymentGoods goods, bool publish, CancellationToken token)
    {
        using var client = CreateClient();
        var access = await tokens.GetAsync(goods.AppId, token);
        try
        {
            WechatApiResponse response = publish
                ? await client.ExecuteXPayStartPublishGoodsAsync(new XPayStartPublishGoodsRequest
                {
                    AccessToken = access, Environment = goods.Environment,
                    PublishItemList = [new() { Id = goods.GoodsId }]
                }, token)
                : await client.ExecuteXPayStartUploadGoodsAsync(new XPayStartUploadGoodsRequest
                {
                    AccessToken = access, Environment = goods.Environment,
                    UploadItemList = [new() { Id = goods.GoodsId, Name = goods.Name, Price = goods.PriceInFen,
                        Remark = goods.Remark, PictureUrl = goods.PictureUrl }]
                }, token);
            using var json = await ValidateAsync(response, access, token);
        }
        catch (CommonException) { throw new InvalidOperationException("WeChat goods request failed."); }
    }

    private GoodsClient CreateClient()
    {
        var weChat = options.Value.WeChat;
        var http = factory.CreateClient(nameof(VirtualPaymentGoodsGateway));
        http.Timeout = TimeSpan.FromSeconds(15);
        return new GoodsClient(new WechatApiClientOptions { AppId = weChat.AppId,
            VirtualPaymentAppKey = weChat.VirtualPayment.Environment == 0
                ? weChat.VirtualPayment.AppKey : weChat.VirtualPayment.SandboxAppKey }, http);
    }

    private async Task<JsonDocument> ValidateAsync(WechatApiResponse response, string access, CancellationToken token)
    {
        var json = JsonDocument.Parse(response.GetRawBytes());
        var error = int.MinValue;
        if (json.RootElement.TryGetProperty("errcode", out var code) && code.TryGetInt32(out error) && error == 0)
            return json;
        if (error is 40001 or 40014 or 42001)
            await tokens.ForgetRejectedAsync(options.Value.WeChat.AppId, access, token);
        json.Dispose();
        // Omit raw messages and URLs; they can contain access tokens.
        throw new InvalidOperationException("WeChat rejected the goods request.");
    }

    private sealed class GoodsClient(WechatApiClientOptions settings, HttpClient http) : WechatApiClient(settings, http);
}
