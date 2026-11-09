namespace Aiursoft.WeChatExam.Models.MiniProgramApi;

/// <summary>
/// Client arguments for wx.requestVirtualPayment. SignData remains a JSON string
/// inside the response; clients must pass it unchanged instead of serializing it again.
/// No AppKey or session_key is returned to the client.
/// </summary>
public class VirtualPaymentParameters
{
    public string Mode => "short_series_goods";
    public required string SignData { get; init; }
    public required string PaySig { get; init; }
    public required string Signature { get; init; }
}
