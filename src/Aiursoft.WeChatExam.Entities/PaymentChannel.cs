namespace Aiursoft.WeChatExam.Entities;

/// <summary>
/// The gateway that owns an order's payment lifecycle, independent of its VIP product.
/// </summary>
public enum PaymentChannel
{
    // Keep zero assigned to the original gateway so historical rows and older writers
    // retain their meaning when the additive column is introduced.
    WeChatJsApi = 0,
    WeChatVirtual = 1
}
