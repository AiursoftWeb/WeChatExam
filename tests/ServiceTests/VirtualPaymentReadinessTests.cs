using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Models.PaymentOrdersViewModels;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VirtualPaymentReadinessTests
{
    private static AppSettings Settings(VirtualPaymentSettings settings) => new()
    {
        AuthProvider = "Local", OIDC = new OidcSettings { Authority = "", ClientId = "", ClientSecret = "", RolePropertyName = "", UserIdentityPropertyName = "", UserDisplayNamePropertyName = "", UsernamePropertyName = "", EmailPropertyName = "" }, Local = new LocalSettings(),
        WeChat = new WeChatSettings { AppId = "wx1234567890abcdef", AppSecret = "secret-not-for-display", VirtualPayment = settings }
    };

    private static VirtualPaymentSettings Config(int environment = 1, string sandboxKey = "sandbox-secret", string liveKey = "",
        Dictionary<string, VirtualPaymentProductSettings>? products = null) => new()
    {
        Environment = environment, OfferId = "offer-not-for-display", SandboxAppKey = sandboxKey, AppKey = liveKey,
        CallbackToken = "callback-secret", CallbackEncodingAesKey = Convert.ToBase64String(new byte[32]).TrimEnd('='),
        Products = products ?? new(StringComparer.OrdinalIgnoreCase)
    };

    private static VipProduct Product(int price = 19800, bool enabled = true) => new()
    {
        Name = "Course <script>", TotalCategory = new TotalCategory { Title = "History" }, PriceInFen = price,
        DurationDays = 30, IsEnabled = enabled
    };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MatchingSetupIsReadOnlyAndDoesNotEnableSalesOrClaimPlatformAcceptance(bool historicalMapping)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync(); var product = Product(); db.Add(product); await db.SaveChangesAsync();
        var config = Config(products: new(StringComparer.OrdinalIgnoreCase) { [product.Id.ToString("D").ToUpperInvariant()] = new() { ProductId = "published-course", PriceInFen = 19800 } });
        if (historicalMapping) config.Products[Guid.NewGuid().ToString("D")] = new() { ProductId = "old-product", PriceInFen = 1000 };
        db.ChangeTracker.Clear();
        var result = await new VirtualPaymentReadinessService(db, Options.Create(Settings(config))).CheckAsync();
        Assert.IsTrue(result.LocalChecksPassed); Assert.AreEqual(historicalMapping ? 1 : 0, result.UnusedMappingCount); Assert.IsFalse(result.SalesEnabled); Assert.IsFalse(result.IosAdviceEnabled);
        Assert.AreEqual(VirtualProductSetupStatus.Configured, result.Products.Single().Status);
        Assert.IsFalse(db.ChangeTracker.HasChanges()); Assert.HasCount(0, db.ChangeTracker.Entries().ToList());
        Assert.AreEqual(0, await db.PaymentOrders.CountAsync()); Assert.AreEqual(0, await db.PaymentLogs.CountAsync());
        Assert.AreEqual(0, await db.CouponUsages.CountAsync()); Assert.AreEqual(0, await db.VipMemberships.CountAsync());
    }

    [TestMethod]
    [DataRow(0, "sandbox-only", "", false)]
    [DataRow(1, "", "live-only", false)]
    [DataRow(2, "sandbox", "live", false)]
    [DataRow(0, "", "live", true)]
    [DataRow(1, "sandbox", "", true)]
    public async Task SigningKeyNeverFallsBackToAnotherEnvironment(int env, string sandbox, string live, bool expected)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var result = await new VirtualPaymentReadinessService(db, Options.Create(Settings(Config(env, sandbox, live)))).CheckAsync();
        Assert.AreEqual(expected, result.Checks.Single(c => c.Code == "key").Passed);
        Assert.IsFalse(result.LocalChecksPassed);
    }

    [TestMethod]
    public async Task ProductProblemsAreReportedWithoutExposingMappingValuesOrChangingData()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var missing = Product(); var mismatch = Product(); var free = Product(0); var duplicate = Product();
        var invalid = Product(); invalid.DurationDays = 0; var disabled = Product(enabled: false);
        db.AddRange(missing, mismatch, free, duplicate, invalid, disabled); await db.SaveChangesAsync();
        var config = Config(products: new(StringComparer.OrdinalIgnoreCase) {
            [mismatch.Id.ToString("D")] = new() { ProductId = "shared-remote-id", PriceInFen = 1 },
            [duplicate.Id.ToString("D")] = new() { ProductId = "shared-remote-id", PriceInFen = 19800 },
            [disabled.Id.ToString("D")] = new() { ProductId = "unused-id", PriceInFen = 19800 },
            ["not-a-local-product-id"] = new() { ProductId = "unused-id-2", PriceInFen = 19800 }
        });
        var result = await new VirtualPaymentReadinessService(db, Options.Create(Settings(config))).CheckAsync();
        Assert.HasCount(5, result.Products); Assert.AreEqual(2, result.UnusedMappingCount); Assert.IsFalse(result.LocalChecksPassed);
        Assert.AreEqual(VirtualProductSetupStatus.MissingMapping, result.Products.Single(p => p.Id == missing.Id).Status);
        Assert.AreEqual(VirtualProductSetupStatus.PriceMismatch, result.Products.Single(p => p.Id == mismatch.Id).Status);
        Assert.AreEqual(VirtualProductSetupStatus.Free, result.Products.Single(p => p.Id == free.Id).Status);
        Assert.AreEqual(VirtualProductSetupStatus.DuplicateMapping, result.Products.Single(p => p.Id == duplicate.Id).Status);
        Assert.AreEqual(VirtualProductSetupStatus.InvalidProduct, result.Products.Single(p => p.Id == invalid.Id).Status);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        foreach (var value in new[] { "secret-not-for-display", "offer-not-for-display", "shared-remote-id", "unused-id", "sandbox-secret", "callback-secret" }) Assert.DoesNotContain(value, json);
        Assert.AreEqual(6, await db.VipProducts.CountAsync());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!")]
    [DataRow("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void CallbackFormatCheckIsSharedWithTheNotificationEndpoint(string key)
    {
        var settings = new VirtualPaymentSettings { CallbackToken = "token", CallbackEncodingAesKey = key };
        Assert.IsFalse(settings.HasValidCallbackCredentials("wx1234567890abcdef"));
        Assert.IsFalse(Config().HasValidCallbackCredentials(""));
        Assert.IsTrue(Config().HasValidCallbackCredentials("wx1234567890abcdef"));
    }
}
