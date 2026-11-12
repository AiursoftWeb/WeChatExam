using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

// Short, restartable steps. The product revision is the durable queue intent;
// remote task intent is committed before issuing any non-idempotent request.
public class VirtualPaymentGoodsSynchronizer(WeChatExamDbContext db, IOptions<AppSettings> options,
    IVirtualPaymentGoodsGateway gateway, ILogger<VirtualPaymentGoodsSynchronizer> logger) : IScopedDependency
{
    public async Task RunOnceAsync(CancellationToken token = default)
    {
        var app = options.Value;
        var settings = app.WeChat.VirtualPayment;
        if (!settings.GoodsSync.Enabled || !app.WeChatEnabled) return;
        if (app.WeChat.AppId.Length is 0 or > 64 || settings.OfferId.Length is 0 or > 64 ||
            settings.Environment is not (0 or 1) ||
            string.IsNullOrWhiteSpace(settings.Environment == 0 ? settings.AppKey : settings.SandboxAppKey) ||
            !Uri.TryCreate(settings.GoodsSync.PictureUrl, UriKind.Absolute, out var picture) || picture.Scheme != "https")
        {
            logger.LogWarning("Virtual goods synchronization configuration is incomplete.");
            return;
        }
        var leaseId = $"{app.WeChat.AppId}:{settings.Environment}";
        if (!await db.VirtualPaymentGoodsLeases.AnyAsync(l => l.Id == leaseId, token))
        {
            var lease = new VirtualPaymentGoodsLease { Id = leaseId };
            db.Add(lease);
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException)
            {
                db.Entry(lease).State = EntityState.Detached;
                if (!await db.VirtualPaymentGoodsLeases.AnyAsync(l => l.Id == leaseId, token)) throw;
            }
        }
        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        if (await db.VirtualPaymentGoodsLeases.Where(l => l.Id == leaseId && l.Until <= now)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Owner, owner)
                    .SetProperty(l => l.Until, now.AddMinutes(5)), token) != 1) return;
        try
        {
            var products = await db.VipProducts.AsNoTracking().Where(p => p.IsEnabled &&
                (p.Type == VipProductType.Category || p.Type == VipProductType.Institution)).ToListAsync(token);
            var fingerprints = products.Select(p => VirtualPaymentGoodsCatalog.Fingerprint(p, settings.GoodsSync.PictureUrl)).ToList();
            var rows = await db.VirtualPaymentGoods.Where(g => g.AppId == app.WeChat.AppId &&
                g.Environment == settings.Environment && g.OfferId == settings.OfferId &&
                (g.State != VirtualGoodsState.Published && g.State != VirtualGoodsState.Failed || fingerprints.Contains(g.Fingerprint))).ToListAsync(token);
            foreach (var product in products.Where(VirtualPaymentGoodsCatalog.IsSupported))
            {
                var fingerprint = VirtualPaymentGoodsCatalog.Fingerprint(product, settings.GoodsSync.PictureUrl);
                var previous = rows.Where(g => g.SourceProductId == product.Id && g.Fingerprint == fingerprint).MaxBy(g => g.Attempt);
                if (previous != null && (previous.State != VirtualGoodsState.Failed ||
                    previous.Error != "task-unconfirmed" || previous.Attempt >= 5 || previous.NextAttemptAt > DateTime.UtcNow)) continue;
                var row = new VirtualPaymentGoods
                {
                    Attempt = (previous?.Attempt ?? 0) + 1,
                    SourceProductId = product.Id, VipProductId = product.Id, AppId = app.WeChat.AppId,
                    Environment = settings.Environment, OfferId = settings.OfferId, Fingerprint = fingerprint,
                    GoodsId = "vp_" + Guid.NewGuid().ToString("N")[..17], Name = product.Name,
                    PriceInFen = product.PriceInFen, DurationDays = product.DurationDays,
                    Remark = $"Membership access for {product.DurationDays} days.", PictureUrl = settings.GoodsSync.PictureUrl
                };
                db.Add(row); rows.Add(row);
            }
            // Never start a second task while recovering the first task's result.
            // Finish in-flight obsolete versions too, without making them current.
            var item = rows.Where(g => g.State is VirtualGoodsState.Uploading or VirtualGoodsState.Publishing or VirtualGoodsState.ReadyToPublish)
                .OrderBy(g => g.CreatedAt).FirstOrDefault();
            if (item == null)
            {
                foreach (var queued in rows.Where(g => g.State == VirtualGoodsState.Queued))
                    if (!products.Any(p => p.Id == queued.SourceProductId && VirtualPaymentGoodsCatalog.IsSupported(p) &&
                        VirtualPaymentGoodsCatalog.Fingerprint(p, settings.GoodsSync.PictureUrl) == queued.Fingerprint))
                    { queued.State = VirtualGoodsState.Failed; queued.Error = "superseded"; }
                item = rows.Where(g => g.State == VirtualGoodsState.Queued).OrderBy(g => g.CreatedAt).FirstOrDefault();
            }
            await db.SaveChangesAsync(token);
            if (item == null || item.NextAttemptAt > DateTime.UtcNow) return;
            try
            {
                await StepAsync(item, leaseId, owner, token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                // Raw SDK/HTTP exceptions can expose signed URLs. Keep only a safe code.
                item.Failures++;
                item.Error = "request-failed";
                item.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.Failures, 6))));
                logger.LogWarning("Virtual goods {GoodsId}: request failed; saved state will be checked before retrying.", item.GoodsId);
            }
            if (await RenewLeaseAsync(leaseId, owner, token)) await db.SaveChangesAsync(token);
        }
        finally
        {
            await db.VirtualPaymentGoodsLeases.Where(l => l.Id == leaseId && l.Owner == owner)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Until, DateTime.UtcNow), CancellationToken.None);
        }
    }

    private async Task StepAsync(VirtualPaymentGoods item, string leaseId, Guid owner, CancellationToken token)
    {
        if (!await RenewLeaseAsync(leaseId, owner, token)) return;
        if (item.State is VirtualGoodsState.Queued or VirtualGoodsState.ReadyToPublish)
        {
            // Upload and publish are platform-wide task slots, not per-product jobs.
            var upload = await gateway.QueryAsync(false, token);
            var publish = await gateway.QueryAsync(true, token);
            if (upload.Status == 1 || publish.Status == 1) return;
            var publishing = item.State == VirtualGoodsState.ReadyToPublish;
            item.State = publishing ? VirtualGoodsState.Publishing : VirtualGoodsState.Uploading;
            item.Error = null;
            item.NextAttemptAt = DateTime.UtcNow.AddSeconds(20);
            if (!await RenewLeaseAsync(leaseId, owner, token)) return;
            await db.SaveChangesAsync(token);
            // A timeout leaves Uploading/Publishing durable. Next run QUERIES;
            // it must not blindly repeat a request whose outcome is unknown.
            await gateway.StartAsync(item, publishing, token);
            return;
        }
        var isPublish = item.State == VirtualGoodsState.Publishing;
        var result = await gateway.QueryAsync(isPublish, token);
        if (result.Status == 1)
        {
            item.NextAttemptAt = DateTime.UtcNow.AddSeconds(30);
            return;
        }
        if (result.Status == 0 || result.Items.Count != 1 || result.Items[0].Id != item.GoodsId)
        {
            // Never activate or overwrite an unconfirmed platform ID. A bounded
            // retry creates a NEW ID and retains this immutable attempt for audit.
            item.State = VirtualGoodsState.Failed;
            item.Error = "task-unconfirmed";
            item.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, item.Attempt)));
            return;
        }
        var observation = result.Items[0];
        if (result.Status != 3 || observation.Status != 2 || !isPublish &&
            (observation.Name != item.Name || observation.Price != item.PriceInFen || observation.Remark != item.Remark))
        {
            item.State = VirtualGoodsState.Failed;
            item.Error = observation.Status == 1 ? "goods-id-conflict" : "platform-rejected";
            return;
        }
        item.Error = null;
        item.Failures = 0;
        item.State = isPublish ? VirtualGoodsState.Published : VirtualGoodsState.ReadyToPublish;
        // The platform documents a propagation delay of about ten minutes.
        // Don't offer a freshly published item before that window has passed.
        if (isPublish) item.PublishedAt = DateTime.UtcNow;
        item.NextAttemptAt = DateTime.UtcNow;
    }

    private async Task<bool> RenewLeaseAsync(string leaseId, Guid owner, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        // Extend before an external write; an expired/stolen lease is never revived.
        return await db.VirtualPaymentGoodsLeases.Where(l => l.Id == leaseId && l.Owner == owner && l.Until > now)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Until, now.AddMinutes(5)), token) == 1;
    }
}
