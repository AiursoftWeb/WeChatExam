using Aiursoft.Canon.BackgroundJobs;

namespace Aiursoft.WeChatExam.Services.BackgroundJobs;

public class SyncVirtualPaymentGoodsJob(VirtualPaymentGoodsSynchronizer synchronizer) : IBackgroundJob
{
    public string Name => "Synchronize WeChat VIP Goods";
    public string Description => "Publishes saved VIP product revisions and recovers unfinished WeChat goods tasks.";
    public Task ExecuteAsync() => synchronizer.RunOnceAsync();
}
