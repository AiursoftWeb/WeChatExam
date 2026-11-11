using Aiursoft.UiStack.Layout;

namespace Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;

public class VirtualPaymentReadinessViewModel : UiStackLayoutViewModel
{
    public VirtualPaymentReadinessViewModel() => PageTitle = "Virtual payment setup";
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
    public bool SalesEnabled { get; init; }
    public bool IosAdviceEnabled { get; init; }
    public int Environment { get; init; }
    public IReadOnlyList<VirtualPaymentSetupCheck> Checks { get; init; } = [];
    public IReadOnlyList<VirtualPaymentProductCheck> Products { get; init; } = [];
    public int UnusedMappingCount { get; init; }
    public bool LocalChecksPassed => Checks.All(c => c.Passed) && Products.Count > 0 &&
        Products.All(p => p.Status is VirtualProductSetupStatus.Configured or VirtualProductSetupStatus.Free or VirtualProductSetupStatus.Published);
}

public record VirtualPaymentSetupCheck(string Code, bool Passed);
public record VirtualPaymentProductCheck(Guid Id, string Name, int PriceInFen, VirtualProductSetupStatus Status);
public enum VirtualProductSetupStatus { Configured, Free, InvalidProduct, MissingMapping, PriceMismatch, DuplicateMapping, Synchronizing, SyncFailed, Published }
