using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Models;

namespace Aiursoft.WeChatExam.Services;

public interface IWeChatService
{
    Task<WeChatSessionResult> CodeToSessionAsync(string code);
}

public class WeChatSessionResult
{
    public bool IsSuccess { get; set; }
    public string? OpenId { get; set; }
    public string? SessionKey { get; set; }
    public string? UnionId { get; set; }
    public int ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public class WeChatService(WechatApiClient wechatApiClient) : IWeChatService
{
    public async Task<WeChatSessionResult> CodeToSessionAsync(string code)
    {
        var request = new SnsJsCode2SessionRequest
        {
            JsCode = code
        };

        var response = await wechatApiClient.ExecuteSnsJsCode2SessionAsync(request);

        return new WeChatSessionResult
        {
            IsSuccess = response.IsSuccessful(),
            OpenId = response.OpenId,
            SessionKey = response.SessionKey,
            UnionId = response.UnionId,
            ErrorCode = response.ErrorCode,
            ErrorMessage = response.ErrorMessage
        };
    }
}
