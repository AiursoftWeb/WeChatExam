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
/// A signed notification identifies a purchase; only server query/reconciliation
/// can authorize membership. Reply success only after its transaction committed.
/// </summary>
public class VirtualPaymentNotificationService(WeChatExamDbContext db, VirtualPaymentReconciliationService reconciliation,
    IOptions<AppSettings> options, ILogger<VirtualPaymentNotificationService> logger) : IScopedDependency
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
        var message = ReadAuthenticatedMessage(body, timestamp, nonce, signature);
        if (message == null) return VirtualPaymentNotificationResult.Rejected;
        // Do not acknowledge refunds, subscriptions or other unimplemented events.
        if (message.Event != "xpay_goods_deliver_notify" || message.MessageType != "event")
            return VirtualPaymentNotificationResult.Rejected;
        try
        {
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
            logger.LogError("Virtual payment notification could not commit its purchase.");
            return VirtualPaymentNotificationResult.Retry;
        }
    }

    private XPayGoodsDeliverNotifyEvent? ReadAuthenticatedMessage(string body, string? timestamp, string? nonce, string? signature)
    {
        if (string.IsNullOrWhiteSpace(body) || Encoding.UTF8.GetByteCount(body) > MaximumBodyBytes) return null;
        try
        {
            var xml = body.TrimStart().StartsWith('<');
            var encrypted = xml ? ParseXml(body).Element("Encrypt")?.Value : ReadEncryptedJson(body);
            if (string.IsNullOrWhiteSpace(encrypted) || !VerifySignature(timestamp, nonce, encrypted, signature)) return null;
            var plain = DecryptMessage(encrypted);
            if (Encoding.UTF8.GetByteCount(plain) > MaximumBodyBytes) return null;
            // Env=0 is meaningful. Do not let an omitted Env default to live.
            int environment;
            if (xml)
            {
                var value = ParseXml(plain).Element("Env")?.Value;
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out environment)) return null;
            }
            else
            {
                using var json = JsonDocument.Parse(plain);
                if (!UniqueJsonObject(json.RootElement) || !json.RootElement.TryGetProperty("Env", out var env) ||
                    !env.TryGetInt32(out environment)) return null;
            }
            if (environment is not (0 or 1)) return null;
            using var client = new WechatApiClient(new WechatApiClientOptions { AppId = options.Value.WeChat.AppId });
            var result = xml ? client.DeserializeEventFromXml<XPayGoodsDeliverNotifyEvent>(plain)
                : client.DeserializeEventFromJson<XPayGoodsDeliverNotifyEvent>(plain);
            return result.Environment == environment && !string.IsNullOrWhiteSpace(result.OpenId) &&
                !string.IsNullOrWhiteSpace(result.OutTradeNumber) ? result : null;
        }
        catch (Exception)
        {
            // This boundary performs parsing/crypto only, with no DB/network effects.
            // The installed SDK can throw several exception types for bad ciphertext.
            return null;
        }
    }

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
