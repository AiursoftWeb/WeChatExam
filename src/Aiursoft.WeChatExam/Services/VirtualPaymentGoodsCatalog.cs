using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

public class VirtualPaymentGoodsCatalog(WeChatExamDbContext db, IOptions<AppSettings> options) : IScopedDependency
{
    public bool Enabled => options.Value.WeChat.VirtualPayment.GoodsSync.Enabled;

    public static string Fingerprint(VipProduct product, string pictureUrl) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { product.Id, product.VirtualGoodsRevision, product.Name,
            product.PriceInFen, product.DurationDays, product.Type, product.TotalCategoryId, product.InstitutionId, pictureUrl }))));

    public static bool IsSupported(VipProduct product) => product.PriceInFen > 0 && product.DurationDays > 0 &&
        product.Name.EnumerateRunes().Count() is > 0 and <= 20 &&
        (product.Type == VipProductType.Category && product.TotalCategoryId != null ||
         product.Type == VipProductType.Institution && product.InstitutionId != null);

    public async Task<VirtualPaymentProductSettings> ResolveAsync(VipProduct product, CancellationToken token = default)
    {
        var weChat = options.Value.WeChat;
        var settings = weChat.VirtualPayment;
        var fingerprint = Fingerprint(product, settings.GoodsSync.PictureUrl);
        var goods = await db.VirtualPaymentGoods.AsNoTracking().Where(g =>
            g.AppId == weChat.AppId && g.Environment == settings.Environment && g.OfferId == settings.OfferId &&
            g.SourceProductId == product.Id && g.Fingerprint == fingerprint)
            .OrderByDescending(g => g.Attempt).FirstOrDefaultAsync(token);
        if (!product.IsEnabled || !IsSupported(product) || goods?.State != VirtualGoodsState.Published ||
            goods.PublishedAt == null || goods.PublishedAt > DateTime.UtcNow.AddMinutes(-10) || goods.PriceInFen != product.PriceInFen)
            throw new VirtualPaymentCheckoutException("This product is being synchronized with WeChat. Please try again later.");
        return new VirtualPaymentProductSettings { GoodsVersionId = goods.Id, ProductId = goods.GoodsId, PriceInFen = goods.PriceInFen };
    }

    public async Task<Dictionary<Guid, VirtualGoodsDisplay>> GetDisplayAsync(IEnumerable<VipProduct> products,
        CancellationToken token = default)
    {
        if (!Enabled) return new();
        var list = products.ToList();
        var ids = list.Select(p => p.Id).ToList();
        var weChat = options.Value.WeChat;
        var settings = weChat.VirtualPayment;
        var fingerprints = list.Select(p => Fingerprint(p, settings.GoodsSync.PictureUrl)).ToList();
        var rows = await db.VirtualPaymentGoods.AsNoTracking().Where(g => ids.Contains(g.SourceProductId) &&
            g.AppId == weChat.AppId && g.Environment == settings.Environment && g.OfferId == settings.OfferId &&
            fingerprints.Contains(g.Fingerprint)).ToListAsync(token);
        return list.ToDictionary(p => p.Id, p =>
        {
            var current = rows.Where(g => g.SourceProductId == p.Id &&
                g.Fingerprint == Fingerprint(p, settings.GoodsSync.PictureUrl)).MaxBy(g => g.Attempt);
            var state = !p.IsEnabled ? "disabled" : p.PriceInFen == 0 ? "free" : !IsSupported(p) ? "invalid" :
                current?.State == VirtualGoodsState.Failed && !(current.Error == "task-unconfirmed" && current.Attempt < 5) ? "failed" :
                current?.State == VirtualGoodsState.Published && current.PublishedAt <= DateTime.UtcNow.AddMinutes(-10) ? "ready" : "pending";
            return new VirtualGoodsDisplay(state, current?.Error, current?.PublishedAt);
        });
    }
}

public sealed record VirtualGoodsDisplay(string State, string? Error, DateTime? PublishedAt);
