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
/// Early refunds remain durable manual-review records until the payment is verified.
/// Reply success only after the corresponding transaction committed.
/// </summary>
public class VirtualPaymentNotificationService(WeChatExamDbContext db, VirtualPaymentReconciliationService reconciliation,
    IOptions<AppSettings> options, ILogger<VirtualPaymentNotificationService> logger, VirtualPaymentRefundService refunds,
    VirtualPaymentIosRefundAdviceService iosAdvice) : IScopedDependency
{
    public const int MaximumBodyBytes = 65536;

    // Optional message credentials must not prevent old application startup.
    // Independent of new-sales Enabled: past purchases still need notifications.
    public bool IsConfigured => options.Value.WeChat.VirtualPayment.HasValidCallbackCredentials(options.Value.WeChat.AppId);

    public bool VerifyEcho(string? timestamp, string? nonce, string? signature) =>
        IsConfigured && VerifySignature(timestamp, nonce, "", signature);

    // Existing callers retain the acknowledgement result. HTTP callers use the
    // reply-aware entry point so an iOS business response is never discarded.
    public async Task<VirtualPaymentNotificationResult> HandleAsync(string body, string? timestamp, string? nonce,
        string? signature, CancellationToken cancellationToken = default) =>
        (await HandleWithReplyAsync(body, timestamp, nonce, signature, cancellationToken)).Result;

    public async Task<VirtualPaymentNotificationResponse> HandleWithReplyAsync(string body, string? timestamp, string? nonce,
        string? signature, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || !db.Database.IsRelational() || db.Database.CurrentTransaction != null ||
            System.Transactions.Transaction.Current != null) return new(VirtualPaymentNotificationResult.Retry);
        var authenticated = ReadAuthenticatedMessage(body, timestamp, nonce, signature);
        if (authenticated == null) return new(VirtualPaymentNotificationResult.Rejected);
        return authenticated.IosRefund != null
            ? await HandleIosRefundAsync(authenticated, cancellationToken)
            : new(await HandleAuthenticatedAsync(authenticated, cancellationToken));
    }

    private async Task<VirtualPaymentNotificationResult> HandleAuthenticatedAsync(AuthenticatedMessage authenticated,
        CancellationToken cancellationToken)
    {
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

    private async Task<VirtualPaymentNotificationResponse> HandleIosRefundAsync(AuthenticatedMessage authenticated,
        CancellationToken cancellationToken)
    {
        if (!options.Value.WeChat.VirtualPayment.IosRefundAdviceEnabled || db.ChangeTracker.HasChanges())
            return new(VirtualPaymentNotificationResult.Retry);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var retrying = false;
            return await db.Database.CreateExecutionStrategy().ExecuteAsync<VirtualPaymentNotificationResponse>(async token =>
            {
                if (retrying) db.ChangeTracker.Clear();
                retrying = true;
                var query = authenticated.IosRefund!;
                // Deliberately no merchant-ID fallback or guessed query_order alias.
                // Activation requires platform confirmation that pay_order_id matches
                // this server-verified platform reference; otherwise stay undecided.
                var advice = await iosAdvice.GetAdviceByPlatformOrderAsync(query.OrderId, query.ProductId,
                    query.Quantity, query.ProvidedStatus, token);
                if (advice is not { ResultCode: 1, Environment: 0 } || advice.PaymentOrderId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(advice.Evidence) || db.ChangeTracker.HasChanges())
                    return new(VirtualPaymentNotificationResult.Retry);
                var original = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == advice.PaymentOrderId, token);
                await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsSqlite()
                    ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted, token);
                await db.Users.Where(u => u.Id == original.UserId)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.ConcurrencyStamp, x => x.ConcurrencyStamp), token);
                var current = await db.PaymentOrders.AsNoTracking().SingleAsync(o => o.Id == original.Id, token);
                if (current.UserId != original.UserId) return new(VirtualPaymentNotificationResult.Retry);
                if (current.CouponId.HasValue)
                    await db.Coupons.Where(c => c.Id == current.CouponId)
                        .ExecuteUpdateAsync(c => c.SetProperty(x => x.IsEnabled, x => x.IsEnabled), token);
                await db.PaymentOrders.Where(o => o.Id == current.Id)
                    .ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, x => x.Status), token);
                // Re-check committed delivery facts under the same locks as refund
                // settlement: an earlier lookup must not override an intervening refund.
                advice = await iosAdvice.GetAdviceForLockedOrderAsync(new VirtualPaymentIosRefundEnquiry(current.Id,
                    query.OrderId, query.ProductId, query.Quantity, query.ProvidedStatus), token);
                if (advice is not { ResultCode: 1, Environment: 0 }) return new(VirtualPaymentNotificationResult.Retry);
                db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = advice.PaymentOrderId,
                    EventType = "VirtualPaymentIosRefundAdvice", RawData = JsonSerializer.Serialize(new
                    {
                        authenticated.RequestHash, query.OrderId, query.ProductId, query.Quantity, query.ProvidedStatus,
                        query.RefundTime, advice.ResultCode, advice.ResultInfo, advice.Evidence
                    }) });
                // Each answer attempt has its own audit record, not a financial
                // settlement. Save failure must never produce an unaudited answer.
                await db.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
                token.ThrowIfCancellationRequested();
                return new(VirtualPaymentNotificationResult.Completed, EncryptIosReply(advice, authenticated.Xml),
                    authenticated.Xml ? "application/xml" : "application/json");
            }, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(VirtualPaymentNotificationResult.Retry);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            logger.LogError("iOS virtual payment refund advice could not be recorded or returned.");
            return new(VirtualPaymentNotificationResult.Retry);
        }
    }

    private string EncryptIosReply(VirtualPaymentIosRefundAdvice advice, bool xml)
    {
        // The nested business response is documented for cloud callbacks. Keep
        // this HTTP transport opt-in until its shape is accepted by the platform.
        var plain = xml ? new XElement("xml", new XElement("ErrCode", 0), new XElement("ErrMsg", "success"),
            new XElement("IosRefundQueryResponse", new XElement("result_code", advice.ResultCode),
                new XElement("result_info", advice.ResultInfo), new XElement("evidence", advice.Evidence))).ToString(SaveOptions.DisableFormatting)
            : JsonSerializer.Serialize(new { ErrCode = 0, ErrMsg = "success", IosRefundQueryResponse = new
            { result_code = advice.ResultCode, result_info = advice.ResultInfo, evidence = advice.Evidence } });
        var text = Encoding.UTF8.GetBytes(plain);
        var app = Encoding.UTF8.GetBytes(options.Value.WeChat.AppId);
        var length = 20 + text.Length + app.Length;
        var padding = 32 - length % 32;
        var bytes = new byte[length + padding];
        RandomNumberGenerator.Fill(bytes.AsSpan(0, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), text.Length);
        text.CopyTo(bytes, 20); app.CopyTo(bytes, 20 + text.Length);
        Array.Fill(bytes, (byte)padding, length, padding);
        var key = Convert.FromBase64String(options.Value.WeChat.VirtualPayment.CallbackEncodingAesKey + "=");
        using var aes = Aes.Create(); aes.Key = key;
        var encrypted = Convert.ToBase64String(aes.EncryptCbc(bytes, key.AsSpan(0, 16), PaddingMode.None));
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var fields = new[] { options.Value.WeChat.VirtualPayment.CallbackToken, timestamp, nonce, encrypted };
        Array.Sort(fields, StringComparer.Ordinal);
        var signature = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(string.Concat(fields)))).ToLowerInvariant();
        return xml ? new XElement("xml", new XElement("Encrypt", encrypted), new XElement("MsgSignature", signature),
            new XElement("TimeStamp", timestamp), new XElement("Nonce", nonce)).ToString(SaveOptions.DisableFormatting)
            : JsonSerializer.Serialize(new { Encrypt = encrypted, MsgSignature = signature, TimeStamp = timestamp, Nonce = nonce });
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
            if (current.Status == PaymentOrderStatus.Refunded && current.PaidAt == null)
            {
                // This path intentionally has no fabricated local payment/grant.
                // Require the exact persisted full refund AND linked resolution.
                return await VirtualPaymentEarlyRefundService.IsCompletedAsync(db, current,
                    new VirtualPaymentRefundConfirmation(current.Id, message.PlatformOrderId,
                        message.PlatformRefundId, message.AmountInFen))
                    ? VirtualPaymentNotificationResult.Completed : VirtualPaymentNotificationResult.Retry;
            }
            if (current.Status is PaymentOrderStatus.Paid or PaymentOrderStatus.Refunded)
            {
                if (current.PaidAt == null) return VirtualPaymentNotificationResult.Retry;
                var payments = await db.PaymentLogs.AsNoTracking().Where(l => l.PaymentOrderId == current.Id &&
                    l.EventType == "VirtualPaymentConfirmed").Select(l => l.RawData).ToListAsync();
                if (payments.Count != 1) return VirtualPaymentNotificationResult.Retry;
                using var proof = JsonDocument.Parse(payments[0]!);
                if (proof.RootElement.GetProperty("OutTradeNo").GetString() != current.OutTradeNo ||
                    proof.RootElement.GetProperty("PlatformOrderId").GetString() != message.PlatformOrderId)
                    return VirtualPaymentNotificationResult.Rejected;
            }
            else if (current.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Failed))
                return VirtualPaymentNotificationResult.Retry;
            // An authenticated refund can precede our payment confirmation. Keep
            // its claimed original payment in the existing manual-review record,
            // without inventing payment evidence or granting/revoking membership.
            // Reconciliation must honor this marker under the same order locks.
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
            if (Text("Event") == "xpay_subscribe_ios_refund_query_notify")
            {
                var order = Text("pay_order_id"); var product = Text("product_id");
                var names = new[] { "pay_order_id", "product_id", "p_count", "provide_status", "refund_time", "order_time" };
                if (root != null && names.Any(n => root.Element(n)?.HasElements == true)) return null;
                if (string.IsNullOrWhiteSpace(order) || order.Length > 128 || string.IsNullOrWhiteSpace(product) || product.Length > 128 ||
                    !int.TryParse(Text("p_count"), NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0 ||
                    !int.TryParse(Text("provide_status"), NumberStyles.None, CultureInfo.InvariantCulture, out var provided) || provided is < 0 or > 2 ||
                    !long.TryParse(Text("refund_time"), NumberStyles.None, CultureInfo.InvariantCulture, out var refundTime) ||
                    !long.TryParse(Text("order_time"), NumberStyles.None, CultureInfo.InvariantCulture, out var orderTime) ||
                    orderTime <= 0 || refundTime < orderTime || refundTime > DateTimeOffset.MaxValue.ToUnixTimeSeconds()) return null;
                return new AuthenticatedMessage(IosRefund: new IosRefundMessage(order, product, quantity, provided, refundTime),
                    Xml: xml, RequestHash: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))));
            }
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

    private sealed record AuthenticatedMessage(XPayGoodsDeliverNotifyEvent? Goods = null, RefundMessage? Refund = null,
        IosRefundMessage? IosRefund = null, bool Xml = false, string? RequestHash = null);
    private sealed record IosRefundMessage(string OrderId, string ProductId, int Quantity, int ProvidedStatus, long RefundTime);
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

public sealed record VirtualPaymentNotificationResponse(VirtualPaymentNotificationResult Result,
    string? Body = null, string ContentType = "text/plain");
