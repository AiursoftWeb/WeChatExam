namespace Aiursoft.WeChatExam.Services;

/// <summary>Keep the gateway code without retaining its URL, body or credentials.</summary>
public sealed class VirtualPaymentGatewayException(int errorCode)
    : InvalidOperationException($"Virtual payment gateway request failed (code {errorCode}).")
{
    public int ErrorCode { get; } = errorCode;
    public bool IsInvalidAccessToken => ErrorCode is 40001 or 40014 or 42001;
}
