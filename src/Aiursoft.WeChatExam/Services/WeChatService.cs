using Senparc.Weixin.WxOpen.AdvancedAPIs.Sns;

namespace Aiursoft.WeChatExam.Services;

public interface IWeChatService
{
    Task<JsCode2JsonResult> CodeToSessionAsync(string appId, string appSecret, string code);
}

public class WeChatService : IWeChatService
{
    public Task<JsCode2JsonResult> CodeToSessionAsync(string appId, string appSecret, string code)
    {
        return SnsApi.JsCode2JsonAsync(appId, appSecret, code);
    }
}
