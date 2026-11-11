using System.Text.RegularExpressions;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Local, read-only setup diagnostics. Never contacts WeChat, exposes settings
/// values, enables sales or certifies real-platform payment/refund acceptance.
/// </summary>
public class VirtualPaymentReadinessService(WeChatExamDbContext db, IOptions<AppSettings> options) : IScopedDependency
{
    public async Task<VirtualPaymentReadinessViewModel> CheckAsync(CancellationToken cancellationToken = default)
    {
        var app = options.Value;
        var settings = app.WeChat.VirtualPayment;
        var products = await db.VipProducts.AsNoTracking()
            .Where(p => p.IsEnabled && (p.Type == VipProductType.Category || p.Type == VipProductType.Institution))
            .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name, p.PriceInFen, p.DurationDays, p.Type,
                p.TotalCategoryId, p.InstitutionId }).ToListAsync(cancellationToken);
        var configured = settings.Products;
        var duplicateIds = configured.Values.Where(p => !string.IsNullOrWhiteSpace(p.ProductId))
            .GroupBy(p => p.ProductId, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var results = products.Select(p =>
        {
            var status = VirtualProductSetupStatus.Configured;
            if (p.PriceInFen < 0 || p.DurationDays <= 0 ||
                (p.Type == VipProductType.Category ? p.TotalCategoryId == null : p.InstitutionId == null))
                status = VirtualProductSetupStatus.InvalidProduct;
            else if (p.PriceInFen == 0) status = VirtualProductSetupStatus.Free;
            else if (!configured.TryGetValue(p.Id.ToString("D"), out var mapping) || string.IsNullOrWhiteSpace(mapping.ProductId))
                status = VirtualProductSetupStatus.MissingMapping;
            else if (mapping.PriceInFen != p.PriceInFen) status = VirtualProductSetupStatus.PriceMismatch;
            else if (duplicateIds.Contains(mapping.ProductId)) status = VirtualProductSetupStatus.DuplicateMapping;
            return new VirtualPaymentProductCheck(p.Id, p.Name, p.PriceInFen, status);
        }).ToList();
        var ids = products.Select(p => p.Id).ToHashSet();
        return new VirtualPaymentReadinessViewModel
        {
            SalesEnabled = settings.Enabled, IosAdviceEnabled = settings.IosRefundAdviceEnabled, Environment = settings.Environment,
            Checks = [
                new("weChat", app.WeChatEnabled),
                new("app", Regex.IsMatch(app.WeChat.AppId, "^wx[0-9a-f]{16}$") && !string.IsNullOrWhiteSpace(app.WeChat.AppSecret)),
                new("environment", settings.Environment is 0 or 1),
                new("offer", !string.IsNullOrWhiteSpace(settings.OfferId)),
                new("key", settings.Environment switch { 0 => !string.IsNullOrWhiteSpace(settings.AppKey),
                    1 => !string.IsNullOrWhiteSpace(settings.SandboxAppKey), _ => false }),
                new("callback", settings.HasValidCallbackCredentials(app.WeChat.AppId))
            ],
            Products = results,
            UnusedMappingCount = configured.Keys.Count(k => !Guid.TryParseExact(k, "D", out var id) || !ids.Contains(id))
        };
    }
}
