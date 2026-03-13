using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.Services;
using Aiursoft.WeChatExam.InMemory;
using Microsoft.EntityFrameworkCore;
// Redundant using removed

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class PaymentServiceTests
{
    private WeChatExamDbContext _dbContext = null!;
    private PaymentOrderService _service = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new InMemoryContext(options);
        _service = new PaymentOrderService(_dbContext);
    }

    [TestMethod]
    public async Task TestGetAllOrdersWithFilter()
    {
        var userId = "user-1";
        var user = new User { Id = userId, UserName = "test", Email = "test@test.com", DisplayName = "test" };
        _dbContext.Users.Add(user);
        
        _dbContext.PaymentOrders.AddRange(
            new PaymentOrder { OutTradeNo = "O1", UserId = userId, Description = "D", Status = PaymentOrderStatus.Paid },
            new PaymentOrder { OutTradeNo = "O2", UserId = userId, Description = "D", Status = PaymentOrderStatus.Pending },
            new PaymentOrder { OutTradeNo = "O3", UserId = "user-2", Description = "D", Status = PaymentOrderStatus.Paid }
        );
        await _dbContext.SaveChangesAsync();

        var paidOrders = await _service.GetAllOrdersAsync(PaymentOrderStatus.Paid);
        Assert.AreEqual(2, paidOrders.Count);

        var user1Orders = await _service.GetAllOrdersAsync(null, userId);
        Assert.AreEqual(2, user1Orders.Count);
        
        var user1PaidOrders = await _service.GetAllOrdersAsync(PaymentOrderStatus.Paid, userId);
        Assert.AreEqual(1, user1PaidOrders.Count);
    }

    [TestMethod]
    public async Task TestGetOrderDetail()
    {
        var userId = "U";
        var user = new User { Id = userId, UserName = "test_detail", Email = "test_detail@test.com", DisplayName = "test_detail" };
        _dbContext.Users.Add(user);
        
        var orderId = Guid.NewGuid();
        var order = new PaymentOrder
        {
            Id = orderId,
            OutTradeNo = "TEST",
            UserId = userId,
            Description = "D",
            Status = PaymentOrderStatus.Pending
        };
        _dbContext.PaymentOrders.Add(order);
        _dbContext.PaymentLogs.Add(new PaymentLog { PaymentOrderId = orderId, EventType = "Created", RawData = "{}" });
        await _dbContext.SaveChangesAsync();

        var detail = await _service.GetOrderDetailAsync(orderId);
        Assert.IsNotNull(detail);
        Assert.AreEqual(1, detail.PaymentLogs.Count);
    }

    [TestMethod]
    public async Task TestGetOrderCount()
    {
        _dbContext.PaymentOrders.AddRange(
            new PaymentOrder { OutTradeNo = "O1", UserId = "U", Description = "D", Status = PaymentOrderStatus.Paid },
            new PaymentOrder { OutTradeNo = "O2", UserId = "U", Description = "D", Status = PaymentOrderStatus.Pending }
        );
        await _dbContext.SaveChangesAsync();

        Assert.AreEqual(1, await _service.GetOrderCountAsync(PaymentOrderStatus.Paid));
        Assert.AreEqual(2, await _service.GetOrderCountAsync());
    }
}
