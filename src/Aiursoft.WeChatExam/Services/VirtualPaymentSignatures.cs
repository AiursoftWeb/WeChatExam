using System.Security.Cryptography;
using System.Text;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// WeChat virtual-payment HMACs. Sign the exact UTF-8 string that will be sent;
/// neither the JSON body nor the textual session_key may be decoded or rewritten.
/// See https://developers.weixin.qq.com/miniprogram/dev/platform-capabilities/business-capabilities/virtual-payment.html
/// </summary>
public static class VirtualPaymentSignatures
{
    public static string SignPayment(string uri, string signData, string appKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(signData);
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        return Sign(appKey, uri + "&" + signData);
    }

    public static string SignUser(string signData, string sessionKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signData);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionKey);
        return Sign(sessionKey, signData);
    }

    private static string Sign(string key, string message) => Convert.ToHexStringLower(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(message)));
}
