using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aiursoft.DbTools;
using Aiursoft.WeChatExam.Configuration;
using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

[TestClass]
public class StaffRefundFlowTests : TestBase
{
    private string _directory = null!;
    private readonly GatewayHandler _handler = new();
    [TestInitialize]
    public override async Task CreateServer()
    {
        _directory = Directory.CreateTempSubdirectory("StaffRefundFlow-").FullName;
        TestStartupWithMockWeChat.SqliteConnectionString = $"Data Source={_directory}/refund.db";
        TestStartupWithMockWeChat.VirtualPaymentHttpClientFactory = new Factory(_handler);
        using var reservation = new TestPortReservation(); Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<TestStartupWithMockWeChat>(["--applicationName", "Aiursoft.WeChatExam", "--AppSettings:WeChat:AppId=test-app",
            "--AppSettings:WeChat:AppSecret=synthetic-app-secret-with-at-least-32-characters", "--AppSettings:WeChat:VirtualPayment:Enabled=true",
            "--AppSettings:WeChat:VirtualPayment:Environment=1", "--AppSettings:WeChat:VirtualPayment:OfferId=offer",
            "--AppSettings:WeChat:VirtualPayment:SandboxAppKey=synthetic-key", $"--Storage:Path={_directory}/storage"], port: reservation.Port);
        reservation.ConfigureServer(Server); await Server.UpdateDbAsync<WeChatExamDbContext>(); await Server.SeedAsync(); await Server.StartAsync();
    }
    [TestCleanup]
    public override async Task CleanServer()
    {
        await base.CleanServer(); TestStartupWithMockWeChat.SqliteConnectionString = null;
        TestStartupWithMockWeChat.VirtualPaymentHttpClientFactory = null; _handler.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
    [TestMethod]
    public async Task AuthorizedWebConfirmationUsesRealSdkAndKeepsTheOriginalIntentThroughSettlement()
    {
        Guid id;
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
            var buyer = new User { UserName = "synthetic-buyer", DisplayName = "Synthetic", MiniProgramOpenId = "saved-openid", SessionKey = "synthetic-session" };
            var product = new VipProduct { Name = "Course", PriceInFen = 19800, DurationDays = 366, TotalCategory = new TotalCategory { Title = "Course" } };
            var order = new PaymentOrder { User = buyer, UserId = buyer.Id, VipProduct = product, OutTradeNo = "FLOW-ORDER", Description = "Synthetic",
                Channel = PaymentChannel.WeChatVirtual, AmountInFen = 200, DiscountInFen = 19600 };
            db.Add(order); await db.SaveChangesAsync(); id = order.Id;
            var settings = scope.ServiceProvider.GetRequiredService<IOptions<AppSettings>>();
            settings.Value.WeChat.VirtualPayment.Products[product.Id.ToString()] = new() { ProductId = "course", PriceInFen = 19800 };
            var snapshot = new VirtualPaymentParameterService(settings).Capture(order, buyer);
            snapshot.Platform = VirtualPaymentPlatform.WeChat; snapshot.PlatformOrderId = "platform-paid"; db.Add(snapshot); await db.SaveChangesAsync();
            await using var tx = await db.Database.BeginTransactionAsync();
            await scope.ServiceProvider.GetRequiredService<PaymentFulfillmentService>().CompleteAsync(order, "transaction");
            db.PaymentLogs.Add(new PaymentLog { PaymentOrderId = order.Id, EventType = "VirtualPaymentConfirmed", RawData = JsonSerializer.Serialize(new { order.OutTradeNo, PlatformOrderId = "platform-paid" }) });
            await db.SaveChangesAsync(); await tx.CommitAsync();
        }
        await LoginAsAdmin(); var page = $"/Refunds/Process/{id}";
        using var sent = await PostForm("/Refunds/Submit", new Dictionary<string,string> { ["id"] = id.ToString(), ["amountInFen"] = "200", ["confirmed"] = "true" }, page);
        Assert.AreEqual(HttpStatusCode.Redirect, sent.StatusCode); Assert.AreEqual(1, _handler.Submissions);
        Assert.Contains("Refund processing", await Http.GetStringAsync(page));
        using var duplicate = await PostForm("/Refunds/Submit", new Dictionary<string,string> { ["id"] = id.ToString(), ["amountInFen"] = "200", ["confirmed"] = "true" }, page);
        Assert.AreEqual(1, _handler.Submissions);
        using var checkedResult = await PostForm("/Refunds/Check", new Dictionary<string,string> { ["id"] = id.ToString() }, page);
        Assert.AreEqual(HttpStatusCode.Redirect, checkedResult.StatusCode);
        Assert.Contains("Refund completed", await Http.GetStringAsync(page));
        using var finalScope = Server!.Services.CreateScope(); var final = finalScope.ServiceProvider.GetRequiredService<WeChatExamDbContext>();
        Assert.AreEqual(VirtualPaymentRefundRequestState.Completed, (await final.VirtualPaymentRefundRequests.SingleAsync()).State);
        Assert.AreEqual(PaymentOrderStatus.Refunded, (await final.PaymentOrders.SingleAsync(o => o.Id == id)).Status);
        var membership = await final.VipMemberships.SingleAsync(); Assert.AreEqual(membership.StartTime, membership.EndTime);
        Assert.AreEqual(1, await final.PaymentLogs.CountAsync(l => l.EventType == "VirtualPaymentRefunded"));
    }
    private sealed class Factory(GatewayHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private sealed class GatewayHandler : HttpMessageHandler
    {
        public int Submissions { get; private set; }
        private JsonNode? _intent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            object response;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/cgi-bin/stable_token": response = new { access_token = "synthetic-token", expires_in = 7200 }; break;
                case "/xpay/refund_order":
                    Submissions++; _intent = body;
                    response = new { errcode = 0, refund_order_id = (string?)body["refund_order_id"], refund_wx_order_id = "platform-refund", pay_order_id = "FLOW-ORDER", pay_wx_order_id = "platform-paid" }; break;
                case "/xpay/query_order" when (string?)body["order_id"] == "FLOW-ORDER":
                    response = new { errcode = 0, order = new { order_id = "FLOW-ORDER", wx_order_id = "platform-paid", wxpay_order_id = "transaction", env_type = 2, order_type = 0,
                        biz_type = 1000, status = 4, order_fee = 200, paid_fee = 200, left_fee = 200 } }; break;
                case "/xpay/query_order" when _intent != null:
                    response = new { errcode = 0, order = new { order_id = (string?)_intent["refund_order_id"], wx_order_id = "platform-refund", env_type = 2, order_type = 1,
                        biz_type = 1000, status = 8, refund_fee = 200, biz_meta = (string?)_intent["biz_meta"] } }; break;
                default: throw new InvalidOperationException("Unexpected external endpoint in isolated flow.");
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response)) };
        }
    }
}
