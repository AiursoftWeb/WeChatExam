using Aiursoft.WeChatExam.Entities;
using Aiursoft.WeChatExam.InMemory;
using Aiursoft.WeChatExam.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.ServiceTests;

[TestClass]
public class VipProductServiceTests
{
    private WeChatExamDbContext _dbContext = null!;
    private VipProductService _service = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        var options = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new InMemoryContext(options);
        _service = new VipProductService(_dbContext);
    }

    [TestMethod]
    public async Task CreateAsync_CategoryVip_SavesCategoryId()
    {
        var catId = Guid.NewGuid();
        var result = await _service.CreateAsync("Cat VIP", VipProductType.Category, catId, 100, 30);
        
        Assert.AreEqual(VipProductType.Category, result.Type);
        Assert.AreEqual(catId, result.CategoryId);
        
        var saved = await _dbContext.VipProducts.FindAsync(result.Id);
        Assert.IsNotNull(saved);
        Assert.AreEqual(catId, saved.CategoryId);
    }

    [TestMethod]
    public async Task CreateAsync_RealExamVip_ClearsCategoryId()
    {
        var catId = Guid.NewGuid();
        var result = await _service.CreateAsync("Real VIP", VipProductType.RealExam, catId, 100, 30);
        
        Assert.AreEqual(VipProductType.RealExam, result.Type);
        Assert.IsNull(result.CategoryId);
        
        var saved = await _dbContext.VipProducts.FindAsync(result.Id);
        Assert.IsNotNull(saved);
        Assert.IsNull(saved.CategoryId);
    }

    [TestMethod]
    public async Task UpdateAsync_ChangesTypeAndCategory()
    {
        var catId = Guid.NewGuid();
        var product = await _service.CreateAsync("Initial", VipProductType.Category, catId, 100, 30);
        
        await _service.UpdateAsync(product.Id, "Updated", VipProductType.RealExam, catId, 200, 60, true);
        
        var updated = await _dbContext.VipProducts.FindAsync(product.Id);
        Assert.AreEqual("Updated", updated!.Name);
        Assert.AreEqual(VipProductType.RealExam, updated.Type);
        Assert.IsNull(updated.CategoryId);
    }

    [TestMethod]
    public async Task GetEnabledAsync_FiltersByType()
    {
        var catId = Guid.NewGuid();
        await _service.CreateAsync("Cat 1", VipProductType.Category, catId, 100, 30);
        await _service.CreateAsync("Cat 2", VipProductType.Category, catId, 100, 30);
        await _service.CreateAsync("Real 1", VipProductType.RealExam, null, 100, 30);
        
        var cats = await _service.GetEnabledAsync(type: VipProductType.Category);
        Assert.AreEqual(2, cats.Count);
        
        var reals = await _service.GetEnabledAsync(type: VipProductType.RealExam);
        Assert.AreEqual(1, reals.Count);
    }
}
