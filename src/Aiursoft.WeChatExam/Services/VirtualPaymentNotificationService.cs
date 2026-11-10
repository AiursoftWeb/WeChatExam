using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SKIT.FlurlHttpClient.Wechat.Api;
using SKIT.FlurlHttpClient.Wechat.Api.Events;

namespace Aiursoft.WeChatExam.Services;

/// <summary>
/// Authenticated mini-program message push, separate from ordinary Tenpay v3.
/// Goods pushes require server query/reconciliation before granting membership.
/// Completed refund pushes bind to that original proof before recovering rights.
/// Reply success only after the corresponding transaction committed.
/// </summary>
public class VirtualPaymentNotificationService(WeChatExamDbContext db, VirtualPaymentReconciliationService reconciliation,
    IOptions<AppSettings> options, ILogger<VirtualPaymentNotificationService> logger, VirtualPaymentRefundService refunds) : IScopedDependency
{
    public const int MaximumBodyBytes = 65536;

    // Optional message credentials must not prevent old application startup.
    // Independent of new-sales Enabled: past purchases still need notifications.
    public bool IsConfigured
    {
        get
        {
            var settings = options.Value.WeChat;
            if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.VirtualPayment.CallbackToken) ||
                settings.VirtualPayment.CallbackToken.Length > 256 || settings.VirtualPayment.CallbackEncodingAesKey.Length != 43) return false;
            try { return Convert.FromBase64String(settings.VirtualPayment.CallbackEncodingAesKey + "=").Length == 32; }
            catch (FormatException) { return false; }
        }
    }

    public bool VerifyEcho(string? timestamp, string? nonce, string? signature) =>
        IsConfigured && VerifySignature(timestamp, nonce, "", signature);

    public async Task<VirtualPaymentNotificationResult> HandleAsync(string body, string? timestamp, string? nonce,
        string? signature, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !db.Database.IsRelational() || db.Database.CurrentTransaction != null ||
            System.Transactions.Transaction.Current != null) return VirtualPaymentNotificationResult.Retry;
        var authenticated = ReadAuthenticatedMessage(body, timestamp, nonce, signature);
        if (authenticated == null) return VirtualPaymentNotificationResult.Rejected;
        try
        {
            if (authenticated.Refund is { } refund) return await HandleRefundAsync(refund, cancellationToken);
            var message = authenticated.Goods!;
            var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o =>
                o.OutTradeNo == message.OutTradeNumber && o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);
            if (order == null) return VirtualPaymentNotificationResult.Rejected;
            var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking()
                .SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
            if (snapshot == null) return VirtualPaymentNotificationResult.Retry;
            var purchase = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            var goods = message.GoodsInfo;
            if (message.OpenId != snapshot.OpenId || message.Environment != purchase.Env || goods == null ||
                goods.ProductId != purchase.ProductId || goods.Quantity != purchase.BuyQuantity ||
                goods.OriginalPrice != purchase.GoodsPrice || goods.Attachment != purchase.Attach ||
                goods.ActualPrice < 0 || goods.ActualPrice > goods.OriginalPrice || message.TeamInfo != null || message.AppleSubscriptionInfo != null)
                return VirtualPaymentNotificationResult.Rejected;
            // Notification prices can reflect platform subsidies. query_order,
            // not this notification or a client callback, verifies payable money.
            var confirmed = await reconciliation.ReconcileAsync(order.OutTradeNo, order.UserId, cancellationToken);
            return confirmed is { Status: PaymentOrderStatus.Paid, PaidAt: not null } && db.Database.CurrentTransaction == null
                ? VirtualPaymentNotificationResult.Completed : VirtualPaymentNotificationResult.Retry;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // Database failures need platform retries, never a success reply.
            // SDK/SQL exceptions may contain credentials or payloads; omit them.
            logger.LogError("Virtual payment notification could not commit its result.");
            return VirtualPaymentNotificationResult.Retry;
        }
    }

    private async Task<VirtualPaymentNotificationResult> HandleRefundAsync(RefundMessage message, CancellationToken cancellationToken)
    {
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o =>
            o.OutTradeNo == message.MerchantOrderId && o.Channel == PaymentChannel.WeChatVirtual, cancellationToken);
        if (order == null) return VirtualPaymentNotificationResult.Rejected;
        var snapshot = await db.VirtualPaymentSnapshots.AsNoTracking()
            .SingleOrDefaultAsync(s => s.PaymentOrderId == order.Id, cancellationToken);
        if (snapshot == null) return VirtualPaymentNotificationResult.Retry;
        var purchase = VirtualPaymentPurchase.Read(order, snapshot, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
        // Refund pushes have no documented Env field. Bind to the ORIGINAL saved
        // purchase and server-confirmed platform payment, never today's sales env.
        if (snapshot.OpenId != message.OpenId || message.AmountInFen > order.AmountInFen ||
            (message.Environment.HasValue && message.Environment != purchase.Env)) return VirtualPaymentNotificationResult.Rejected;
        if (order.Status is not (PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded) || order.PaidAt == null)
            return VirtualPaymentNotificationResult.Retry;
        var payments = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == order.Id &&
            l.EventType == "VirtualPaymentConfirmed").Select(l => l.RawData).ToListAsync(cancellationToken);
        if (payments.Count != 1) return VirtualPaymentNotificationResult.Retry;
        using (var proof = JsonDocument.Parse(payments[0]!))
            if (proof.RootElement.GetProperty("OutTradeNo").GetString() != order.OutTradeNo ||
                proof.RootElement.GetProperty("PlatformOrderId").GetString() != message.PlatformOrderId)
                return VirtualPaymentNotificationResult.Rejected;

        var retrying = false;
        // Authenticated RetCode=0 + completion timestamp is refund proof. The
        // purchase query's broad refund/reversal status is NOT proof of success.
        // Persist after verification even if the HTTP sender disconnects.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (retrying) db.ChangeTracker.Clear();
            retrying = true;
            await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            await db.Users.Where(u => u.Id == order.UserId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ConcurrencyStamp, x => x.ConcurrencyStamp));
            var current = await db.PaymentOrders.SingleAsync(o => o.Id == order.Id && o.UserId == order.UserId);
            if (current.CouponId.HasValue)
                await db.Coupons.Where(c => c.Id == current.CouponId)
                    .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsEnabled, x => x.IsEnabled));
            await db.PaymentOrders.Where(o => o.Id == current.Id)
                .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, x => x.Status));
            await db.Entry(current).ReloadAsync();
            var saved = await db.VirtualPaymentSnapshots.AsNoTracking().SingleAsync(s => s.PaymentOrderId == current.Id);
            if (current.Channel != PaymentChannel.WeChatVirtual || current.OutTradeNo != order.OutTradeNo ||
                current.AmountInFen != order.AmountInFen || current.DiscountInFen != order.DiscountInFen ||
                current.CouponId != order.CouponId || current.VipProductId != order.VipProductId ||
                saved.AppId != snapshot.AppId || saved.OpenId != snapshot.OpenId || saved.SignData != snapshot.SignData)
                throw new InvalidOperationException("The virtual purchase changed during refund settlement.");
            _ = VirtualPaymentPurchase.Read(current, saved, options.Value.WeChat.AppId, options.Value.WeChat.VirtualPayment.OfferId);
            if (current.Status is not (PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded) || current.PaidAt == null)
                return VirtualPaymentNotificationResult.Retry;
            var outcome = await refunds.CompleteAsync(current, new VirtualPaymentRefundConfirmation(current.Id,
                message.PlatformOrderId, message.PlatformRefundId, message.AmountInFen));
            await transaction.CommitAsync();
            // A durable manual-review marker also acknowledges delivery, NOT that
            // uncertain rights were revoked. Its operator workflow is separate.
            if (outcome == VirtualPaymentRefundOutcome.ReviewRequired)
                logger.LogWarning("Virtual payment refund requires review for {OutTradeNo}.", current.OutTradeNo);
            return VirtualPaymentNotificationResult.Completed;
        });
    }

    private AuthenticatedMessage? ReadAuthenticatedMessage(string body, string? timestamp, string? nonce, string? signature)
    {
        if (string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes) return null;
        try
        {
            var xml = body.TrimStart().StartsWith('<');
            var encrypted = xml ? ParseXml(body).Element("Encrypt")?.Value : ReadEncryptedJson(body);
            if (string.IsNullOrWhiteSpace(encrypted) || !VerifySignature(timestamp, nonce, encrypted, signature)) return null;
            var plain = DecryptMessage(encrypted);
            if (Encoding.UTF8.GetByteCount(plain) > MaximumBodyBytes) return null;
            var root = xml ? ParseXml(plain) : null;
            using var json = xml ? null : JsonDocument.Parse(plain);
            if (json != null && !UniqueJsonObject(json.RootElement)) return null;
            string? Text(string name) => root != null ? root.Element(name)?.Value :
                json!.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            long? Number(string name) => root != null
                ? long.TryParse(root.Element(name)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null
                : json!.RootElement.TryGetProperty(name, out var value) && value.TryGetInt64(out var jsonNumber) ? jsonNumber : null;
            bool Has(string name) => root != null ? root.Element(name) != null : json!.RootElement.TryGetProperty(name, out _);
            if (Text("MsgType") != "event") return null;
            if (Text("Event") == "xpay_refund_notify")
            {
                var amount = Number("RefundFee");
                var start = Number("RefundStartTimestamp");
                var success = Number("RefundSuccTimestamp");
                var environment = Number("Env");
                var fields = new[] { Text("OpenId"), Text("MchOrderId"), Text("WxOrderId"), Text("WxRefundId") };
                if (Number("RetCode") != 0 || amount is not (> 0 and <= int.MaxValue) || start is not > 0 ||
                    success == null || success < start || success > DateTimeOffset.MaxValue.ToUnixTimeSeconds() ||
                    fields.Any(f => string.IsNullOrWhiteSpace(f) || f.Length > 128) || Has("TeamInfo") ||
                    (Has("Env") && environment is not (0 or 1))) return null;
                return new AuthenticatedMessage(Refund: new RefundMessage(fields[0]!, fields[1]!, fields[2]!, fields[3]!,
                    (int)amount.Value, (int?)environment));
            }
            if (Text("Event") != "xpay_goods_deliver_notify") return null;
            // Env=0 is meaningful for goods. Missing Env must still be rejected.
            var goodsEnvironment = Number("Env");
            if (goodsEnvironment is not (0 or 1)) return null;
            using var client = new WechatApiClient(new WechatApiClientOptions { AppId = options.Value.WeChat.AppId });
            var result = xml ? client.DeserializeEventFromXml<XPayGoodsDeliverNotifyEvent>(plain)
                : client.DeserializeEventFromJson<XPayGoodsDeliverNotifyEvent>(plain);
            return result.Environment == goodsEnvironment && !string.IsNullOrWhiteSpace(result.OpenId) &&
                !string.IsNullOrWhiteSpace(result.OutTradeNumber) ? new AuthenticatedMessage(Goods: result) : null;
        }
        catch (Exception)
        {
            // Parsing/crypto only. Never expose ciphertext, session keys or SDK errors.
            return null;
        }
    }

    private sealed record AuthenticatedMessage(XPayGoodsDeliverNotifyEvent? Goods = null, RefundMessage? Refund = null);
    private sealed record RefundMessage(string OpenId, string MerchantOrderId, string PlatformOrderId,
        string PlatformRefundId, int AmountInFen, int? Environment);

    private bool VerifySignature(string? timestamp, string? nonce, string encrypted, string? signature)
    {
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > 256 || signature?.Length != 40 ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0) return false;
        try
        {
            var fields = new[] { options.Value.WeChat.VirtualPayment.CallbackToken, timestamp, nonce, encrypted };
            Array.Sort(fields, StringComparer.Ordinal);
            var expected = SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(fields)));
            return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature));
        }
        catch (FormatException) { return false; }
    }

    private string DecryptMessage(string encrypted)
    {
        // The installed SDK's public decrypt helper discards the AppId footer.
        // Parse Tencent's envelope explicitly: random[16] + length[4, big-endian]
        // + UTF-8 message + AppId, with CBC and 32-byte PKCS#7 padding.
        var key = Convert.FromBase64String(options.Value.WeChat.VirtualPayment.CallbackEncodingAesKey + "=");
        var cipher = Convert.FromBase64String(encrypted);
        if (cipher.Length < 32 || cipher.Length % 16 != 0) throw new FormatException();
        using var aes = Aes.Create();
        aes.Key = key;
        var bytes = aes.DecryptCbc(cipher, key.AsSpan(0, 16), PaddingMode.None);
        var padding = bytes[^1];
        if (padding is < 1 or > 32 || bytes.Length < padding || bytes.TakeLast(padding).Any(b => b != padding))
            throw new FormatException();
        var payload = bytes.AsSpan(0, bytes.Length - padding);
        if (payload.Length < 20) throw new FormatException();
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(payload.Slice(16, 4));
        if (length < 0 || length > payload.Length - 20 ||
            !payload[(20 + length)..].SequenceEqual(Encoding.UTF8.GetBytes(options.Value.WeChat.AppId))) throw new FormatException();
        return new UTF8Encoding(false, true).GetString(payload.Slice(20, length));
    }

    private static string? ReadEncryptedJson(string body)
    {
        using var json = JsonDocument.Parse(body);
        return UniqueJsonObject(json.RootElement) && json.RootElement.TryGetProperty("Encrypt", out var encrypted)
            ? encrypted.GetString() : null;
    }

    private static bool UniqueJsonObject(JsonElement element) => element.ValueKind == JsonValueKind.Object &&
        element.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == element.EnumerateObject().Count();

    private static XElement ParseXml(string body)
    {
        using var reader = XmlReader.Create(new StringReader(body), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBodyBytes });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "xml" || root.DescendantsAndSelf().Any(e => e.Elements().GroupBy(x => x.Name).Any(g => g.Count() > 1)))
            throw new XmlException("Invalid virtual payment notification.");
        return root;
    }
}

public enum VirtualPaymentNotificationResult { Rejected, Retry, Completed }
