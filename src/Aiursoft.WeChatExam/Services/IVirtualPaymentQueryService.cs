using Aiursoft.WeChatExam.Entities;

namespace Aiursoft.WeChatExam.Services;

public interface IVirtualPaymentQueryService
{
    Task<VirtualPaymentQueryResult> QueryAsync(PaymentOrder order, VirtualPaymentSnapshot snapshot,
        string accessToken, CancellationToken cancellationToken = default);
}
