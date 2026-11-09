using System.Text.Json;
using System.Text.Json.Nodes;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Models.MiniProgramApi;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentParameterTests
{
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string SessionKey = "9hAb/NEYUlkaMBEsmFgzig==";
    private const string OrderNumber = "ORDER-20261109-ABC123";

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublishedProductMappingCanBeLoadedFromConfiguration(bool upperCase)
    {
        var productKey = upperCase ? ProductId.ToString().ToUpperInvariant() : ProductId.ToString();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Enabled"] = "true", ["Environment"] = "1", ["OfferId"] = "123", ["SandboxAppKey"] = "sandbox-secret",
            [$"Products:{productKey}:ProductId"] = "history-membership", [$"Products:{productKey}:PriceInFen"] = "19800"
        }).Build();
        var settings = configuration.Get<VirtualPaymentSettings>();
        Assert.IsNotNull(settings);
        Assert.HasCount(1, settings.Products);
        var result = Prepare(CreateService(settings), CreateOrder(), CreateBuyer());
        using var json = JsonDocument.Parse(result.SignData);
        Assert.AreEqual("history-membership", json.RootElement.GetProperty("productId").GetString());
    }

    [TestMethod]
    public void OfficialSignatureExamplesMatch()
    {
        // Published WeChat examples: fixed body, UTF-8 textual keys and lowercase hex.
        const string body = "{\"openid\": \"xxx\", \"user_ip\": \"127.0.0.1\", \"env\": 0}";
        Assert.AreEqual("c37809f27c6d7fd1837ad2500a04512b66b34fd793a39a385fade56dca89a4b5",
            VirtualPaymentSignatures.SignPayment("/xpay/query_user_balance", body, "12345"));
        Assert.AreEqual("089d9e8dc5d308977360c4b79ec600a93d736802802a807d634192328032f6c7",
            VirtualPaymentSignatures.SignUser(body, SessionKey));
    }

    [TestMethod]
    [DataRow(0, "dbc52aaf57f1eb23ce7727ba52eefbf34476ff03cc937a8647a57bac33168cca", "b654e1258e15c8d2254f6cb25c965c794fc1abef8af3046c151287469e7e96cf")]
    [DataRow(1, "eec177a24fe24c44be9b116d38254220f1eeef220958ecb0f47d9169b87b2ab3", "6d81ebffc1b9535a24e12a4719444a845e6d990a46c37ad2849ffaba7c73aa4d")]
    public void ClientParametersMatchIndependentVectorsAndKeepSignedString(int environment, string paySig, string signature)
    {
        var service = CreateService(CreateSettings(environment));
        var order = CreateOrder();
        var parameters = Prepare(service, order, CreateBuyer());
        var expected = "{\"offerId\":\"123\",\"buyQuantity\":1,\"env\":" + environment +
            ",\"currencyType\":\"CNY\",\"productId\":\"history-membership\",\"goodsPrice\":19800,\"activitySellingPrice\":6800,\"outTradeNo\":\"ORDER-20261109-ABC123\",\"attach\":\"11111111222233334444555555555555\"}";
        Assert.AreEqual(expected, parameters.SignData);
        Assert.AreEqual(paySig, parameters.PaySig);
        Assert.AreEqual(signature, parameters.Signature);
        Assert.AreEqual("short_series_goods", parameters.Mode);

        // The API uses Newtonsoft; verify the outer response leaves SignData as
        // the same string. The client must not parse and stringify it before paying.
        var outer = Newtonsoft.Json.JsonConvert.SerializeObject(parameters, new Newtonsoft.Json.JsonSerializerSettings
        { ContractResolver = new CamelCasePropertyNamesContractResolver() });
        var json = JObject.Parse(outer);
        Assert.AreEqual(JTokenType.String, json["signData"]!.Type);
        Assert.AreEqual(expected, (string?)json["signData"]);
        Assert.AreEqual(4, json.Properties().Count());
        Assert.DoesNotContain("sandbox-secret", outer);
        Assert.DoesNotContain("live-secret", outer);
        Assert.DoesNotContain(SessionKey, outer);
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        Assert.IsNull(order.PaidAt);
        Assert.IsNull(order.WechatTransactionId);
    }

    [TestMethod]
    public void UndiscountedPurchaseOmitsActivityPrice()
    {
        var result = Prepare(CreateService(CreateSettings()), CreateOrder(amount: 19800, discount: 0), CreateBuyer());
        using var json = JsonDocument.Parse(result.SignData);
        Assert.AreEqual(19800, json.RootElement.GetProperty("goodsPrice").GetInt32());
        Assert.IsFalse(json.RootElement.TryGetProperty("activitySellingPrice", out _));
    }

    [TestMethod]
    public void SavedAmountsAreNotRepricedFromCurrentCatalog()
    {
        var order = CreateOrder();
        order.VipProduct = new VipProduct { Id = ProductId, Name = "Changed price", PriceInFen = 50000 };
        using var json = JsonDocument.Parse(Prepare(CreateService(CreateSettings()), order, CreateBuyer()).SignData);
        Assert.AreEqual(19800, json.RootElement.GetProperty("goodsPrice").GetInt32());
        Assert.AreEqual(6800, json.RootElement.GetProperty("activitySellingPrice").GetInt32());
        Assert.AreEqual(13000, order.DiscountInFen);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void MissingSelectedKeyNeverFallsBackToOtherEnvironment(int environment)
    {
        var settings = CreateSettings(environment, liveKey: environment == 0 ? "" : "live-secret",
            sandboxKey: environment == 1 ? "" : "sandbox-secret");
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(settings), CreateOrder(), CreateBuyer()));
    }

    [TestMethod]
    public void LegacyConfigurationKeepsVirtualPaymentsDisabled()
    {
        var defaults = new WeChatSettings();
        Assert.IsFalse(defaults.VirtualPayment.Enabled);
        Assert.AreEqual(1, defaults.VirtualPayment.Environment);
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(defaults.VirtualPayment), CreateOrder(), CreateBuyer()));
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings(), weChatEnabled: false), CreateOrder(), CreateBuyer()));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(2)]
    public void UnknownEnvironmentCannotBeSigned(int environment) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings(environment)), CreateOrder(), CreateBuyer()));

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void IncompleteMerchantOrProductConfigurationCannotBeSigned(int missing)
    {
        var settings = CreateSettings(offer: missing == 0 ? "" : "123", product: missing == 1 ? "" : "history-membership", price: missing == 2 ? 0 : 19800);
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(settings), CreateOrder(), CreateBuyer()));
    }

    [TestMethod]
    public void MissingProductMappingCannotBeSigned() =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings()), CreateOrder(product: Guid.NewGuid()), CreateBuyer()));

    [TestMethod]
    [DataRow(PaymentChannel.WeChatJsApi, PaymentOrderStatus.Pending, false)]
    [DataRow((PaymentChannel)99, PaymentOrderStatus.Pending, false)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Pending, true)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Paid, false)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Closed, false)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Failed, false)]
    [DataRow(PaymentChannel.WeChatVirtual, PaymentOrderStatus.Refunded, false)]
    public void WrongChannelExpiredOrCompletedOrderCannotBeSigned(PaymentChannel channel, PaymentOrderStatus status, bool expired) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings()), CreateOrder(channel, status, expired), CreateBuyer()));

    [TestMethod]
    [DataRow("short")]
    [DataRow("_12345678")]
    [DataRow("ORDER WITH SPACE")]
    [DataRow("ORDER/20261109")]
    [DataRow("订单20261109001")]
    [DataRow("123456789012345678901234567890123")]
    public void UnsupportedOrderNumberCannotBeSigned(string number) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings()), CreateOrder(number: number), CreateBuyer()));

    [TestMethod]
    [DataRow("A1234567")]
    [DataRow("A1234567890123456789012345678901")]
    [DataRow("A_-|*@12")]
    public void SupportedOrderNumberCanBeSigned(string number)
    {
        using var json = JsonDocument.Parse(Prepare(CreateService(CreateSettings()), CreateOrder(number: number), CreateBuyer()).SignData);
        Assert.AreEqual(number, json.RootElement.GetProperty("outTradeNo").GetString());
    }

    [TestMethod]
    [DataRow(0, 19800)]
    [DataRow(-1, 19801)]
    [DataRow(19801, -1)]
    [DataRow(6801, 13000)]
    [DataRow(int.MaxValue, int.MaxValue)]
    public void ZeroNegativeMismatchedOrOverflowingAmountsCannotBeSigned(int amount, int discount) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings()), CreateOrder(amount: amount, discount: discount), CreateBuyer()));

    [TestMethod]
    [DataRow("other-buyer", "openid", SessionKey)]
    [DataRow("buyer", "", SessionKey)]
    [DataRow("buyer", "openid", "")]
    public void WrongBuyerOrMissingLoginStateCannotBeSigned(string id, string openId, string sessionKey)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => Prepare(CreateService(CreateSettings()), CreateOrder(), CreateBuyer(id, openId, sessionKey)));
        Assert.DoesNotContain("sandbox-secret", error.Message);
        Assert.DoesNotContain("live-secret", error.Message);
        Assert.DoesNotContain(SessionKey, error.Message);
    }

    [TestMethod]
    public async Task SavedSnapshotSurvivesReloadAndCatalogChangesWithoutChangingThePurchase()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options;
        string originalJson;
        string originalPaySig;
        string originalUserSignature;
        await using (var db = new SqliteContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var order = CreateOrder();
            var buyer = CreateBuyer();
            var product = new VipProduct { Id = ProductId, Name = "Membership", PriceInFen = 19800,
                TotalCategory = new TotalCategory { Title = "History" }, DurationDays = 30 };
            var service = CreateService(CreateSettings());
            var snapshot = service.Capture(order, buyer);
            var parameters = service.Prepare(order, buyer, snapshot);
            originalJson = snapshot.SignData;
            originalPaySig = parameters.PaySig;
            originalUserSignature = parameters.Signature;
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.AddRange(buyer, product, order, snapshot);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var reopened = new SqliteContext(options);
        var saved = await reopened.VirtualPaymentSnapshots.SingleAsync();
        var savedOrder = await reopened.PaymentOrders.SingleAsync();
        var savedBuyer = await reopened.Users.SingleAsync();
        // A refreshed login changes only the user signature, not the purchase.
        savedBuyer.SessionKey = "another-current-session";
        var changedSettings = CreateSettings(environment: 0, product: "replacement-product", price: 50000);
        changedSettings.Products.Clear();
        var resumed = CreateService(changedSettings).Prepare(savedOrder, savedBuyer, saved);
        Assert.AreEqual(originalJson, saved.SignData);
        Assert.AreEqual(originalJson, resumed.SignData);
        Assert.AreEqual(originalPaySig, resumed.PaySig);
        Assert.AreNotEqual(originalUserSignature, resumed.Signature);
        using var json = JsonDocument.Parse(resumed.SignData);
        Assert.AreEqual(1, json.RootElement.GetProperty("env").GetInt32());
        Assert.AreEqual("history-membership", json.RootElement.GetProperty("productId").GetString());
        Assert.AreEqual(19800, json.RootElement.GetProperty("goodsPrice").GetInt32());
        Assert.AreEqual(6800, json.RootElement.GetProperty("activitySellingPrice").GetInt32());
        Assert.DoesNotContain(SessionKey, saved.SignData);
        Assert.DoesNotContain("sandbox-secret", saved.SignData);
        Assert.DoesNotContain("live-secret", saved.SignData);
        Assert.AreEqual(PaymentOrderStatus.Pending, savedOrder.Status);
        Assert.IsNull(savedOrder.PaidAt);
        Assert.AreEqual(0, await reopened.VipMemberships.CountAsync());
        Assert.AreEqual(0, await reopened.CouponUsages.CountAsync());
        Assert.AreEqual(0, await reopened.Changes.CountAsync());
        Assert.AreEqual(0, await reopened.PaymentLogs.CountAsync());
    }

    [TestMethod]
    [DataRow("offerId", "another-offer")]
    [DataRow("buyQuantity", 2)]
    [DataRow("env", 2)]
    [DataRow("currencyType", "USD")]
    [DataRow("productId", "")]
    [DataRow("goodsPrice", 19801)]
    [DataRow("activitySellingPrice", 6801)]
    [DataRow("outTradeNo", "ANOTHER-ORDER")]
    [DataRow("attach", "another-reference")]
    public void CorruptSnapshotCannotBeSigned(string field, object value)
    {
        var service = CreateService(CreateSettings());
        var order = CreateOrder();
        var buyer = CreateBuyer();
        var snapshot = service.Capture(order, buyer);
        var json = JsonNode.Parse(snapshot.SignData)!.AsObject();
        json[field] = JsonNode.Parse(JsonSerializer.Serialize(value));
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(order, buyer, CopySnapshot(snapshot, signData: json.ToJsonString())));
        Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void SnapshotForAnotherOrderAppOrBuyerCannotBeSigned(int mismatch)
    {
        var service = CreateService(CreateSettings());
        var order = CreateOrder();
        var buyer = CreateBuyer();
        var snapshot = service.Capture(order, buyer);
        var mismatched = CopySnapshot(snapshot, orderId: mismatch == 0 ? Guid.NewGuid() : null,
            appId: mismatch == 1 ? "another-app" : null, openId: mismatch == 2 ? "another-openid" : null);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(order, buyer, mismatched));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("not-json")]
    public void InvalidSnapshotIsRejectedWithoutExposingItsPayload(string json)
    {
        var service = CreateService(CreateSettings());
        var order = CreateOrder();
        var buyer = CreateBuyer();
        var snapshot = service.Capture(order, buyer);
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(order, buyer, CopySnapshot(snapshot, signData: json)));
        Assert.DoesNotContain(SessionKey, error.Message);
        Assert.DoesNotContain("sandbox-secret", error.Message);
    }

    [TestMethod]
    public void SavedEnvironmentCannotFallBackToTheNewActiveEnvironmentKey()
    {
        var order = CreateOrder();
        var buyer = CreateBuyer();
        var snapshot = CreateService(CreateSettings()).Capture(order, buyer);
        var switched = CreateService(CreateSettings(environment: 0, sandboxKey: ""));
        Assert.ThrowsExactly<InvalidOperationException>(() => switched.Prepare(order, buyer, snapshot));
        var otherMerchant = CreateService(CreateSettings(offer: "another-offer"));
        Assert.ThrowsExactly<InvalidOperationException>(() => otherMerchant.Prepare(order, buyer, snapshot));
    }

    [TestMethod]
    [DataRow(PaymentOrderStatus.Paid, false)]
    [DataRow(PaymentOrderStatus.Closed, false)]
    [DataRow(PaymentOrderStatus.Failed, false)]
    [DataRow(PaymentOrderStatus.Refunded, false)]
    [DataRow(PaymentOrderStatus.Pending, true)]
    public void SavedSnapshotDoesNotBypassCurrentOrderStatus(PaymentOrderStatus status, bool expired)
    {
        var service = CreateService(CreateSettings());
        var buyer = CreateBuyer();
        var snapshot = service.Capture(CreateOrder(), buyer);
        Assert.ThrowsExactly<InvalidOperationException>(() => service.Prepare(CreateOrder(status: status, expired: expired), buyer, snapshot));
    }

    private static VirtualPaymentSnapshot CopySnapshot(VirtualPaymentSnapshot snapshot, Guid? orderId = null,
        string? appId = null, string? openId = null, string? signData = null) => new()
        { PaymentOrderId = orderId ?? snapshot.PaymentOrderId, AppId = appId ?? snapshot.AppId,
            OpenId = openId ?? snapshot.OpenId, SignData = signData ?? snapshot.SignData };

    private static VirtualPaymentParameters Prepare(
        VirtualPaymentParameterService service, PaymentOrder order, User buyer) =>
        service.Prepare(order, buyer, service.Capture(order, buyer));

    private static User CreateBuyer(string id = "buyer", string openId = "openid", string sessionKey = SessionKey) =>
        new() { Id = id, UserName = id, DisplayName = "Buyer", MiniProgramOpenId = openId, SessionKey = sessionKey };

    private static PaymentOrder CreateOrder(PaymentChannel channel = PaymentChannel.WeChatVirtual,
        PaymentOrderStatus status = PaymentOrderStatus.Pending, bool expired = false,
        int amount = 6800, int discount = 13000, string number = OrderNumber, Guid? product = null) =>
        new() { Id = Guid.Parse("11111111-2222-3333-4444-555555555555"), UserId = "buyer", VipProductId = product ?? ProductId,
            OutTradeNo = number, Description = "Reserved membership", AmountInFen = amount, DiscountInFen = discount,
            Channel = channel, Status = status, ExpiredAt = DateTime.UtcNow.AddMinutes(expired ? -1 : 30) };

    private static VirtualPaymentSettings CreateSettings(int environment = 1, string liveKey = "live-secret",
        string sandboxKey = "sandbox-secret", string offer = "123", string product = "history-membership", int price = 19800) =>
        new() { Enabled = true, Environment = environment, OfferId = offer, AppKey = liveKey, SandboxAppKey = sandboxKey,
            Products = new Dictionary<string, VirtualPaymentProductSettings> { [ProductId.ToString()] = new() { ProductId = product, PriceInFen = price } } };

    private static VirtualPaymentParameterService CreateService(VirtualPaymentSettings settings, bool weChatEnabled = true) =>
        new(Options.Create(new AppSettings
        {
            AuthProvider = "Local", Local = new LocalSettings(), WeChatEnabled = weChatEnabled,
            OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "",
                UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" },
            WeChat = new WeChatSettings { AppId = "test-app", VirtualPayment = settings }
        }));
}
