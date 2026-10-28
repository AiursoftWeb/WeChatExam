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
    public async Task CreateAsync_CategoryVip_SavesTotalCategoryId()
    {
        var catId = Guid.NewGuid();
        _dbContext.TotalCategories.Add(new TotalCategory { Id = catId, Title = "Course" });
        await _dbContext.SaveChangesAsync();
        var result = await _service.CreateAsync("Cat VIP", VipProductType.Category, catId, 100, 30);
        
        Assert.AreEqual(VipProductType.Category, result.Type);
        Assert.AreEqual(catId, result.TotalCategoryId);
        
        var saved = await _dbContext.VipProducts.FindAsync(result.Id);
        Assert.IsNotNull(saved);
        Assert.AreEqual(catId, saved.TotalCategoryId);
    }

    [TestMethod]
    public async Task CreateAsync_RealExamVip_ClearsTotalCategoryId()
    {
        var catId = Guid.NewGuid();
        _dbContext.TotalCategories.Add(new TotalCategory { Id = catId, Title = "Course" });
        await _dbContext.SaveChangesAsync();
        var result = await _service.CreateAsync("Real VIP", VipProductType.RealExam, catId, 100, 30);
        
        Assert.AreEqual(VipProductType.RealExam, result.Type);
        Assert.IsNull(result.TotalCategoryId);
        
        var saved = await _dbContext.VipProducts.FindAsync(result.Id);
        Assert.IsNotNull(saved);
        Assert.IsNull(saved.TotalCategoryId);
    }

    [TestMethod]
    public async Task UpdateAsync_ChangesTypeAndCategory()
    {
        var catId = Guid.NewGuid();
        _dbContext.TotalCategories.Add(new TotalCategory { Id = catId, Title = "Course" });
        await _dbContext.SaveChangesAsync();
        var product = await _service.CreateAsync("Initial", VipProductType.Category, catId, 100, 30);
        
        await _service.UpdateAsync(product.Id, "Updated", VipProductType.RealExam, catId, 200, 60, true);
        
        var updated = await _dbContext.VipProducts.FindAsync(product.Id);
        Assert.AreEqual("Updated", updated!.Name);
        Assert.AreEqual(VipProductType.RealExam, updated.Type);
        Assert.IsNull(updated.TotalCategoryId);
    }

    [TestMethod]
    public async Task GetEnabledAsync_FiltersByType()
    {
        var catId = Guid.NewGuid();
        _dbContext.TotalCategories.Add(new TotalCategory { Id = catId, Title = "Course" });
        await _dbContext.SaveChangesAsync();
        await _service.CreateAsync("Cat 1", VipProductType.Category, catId, 100, 30);
        await _service.CreateAsync("Cat 2", VipProductType.Category, catId, 100, 30);
        await _service.CreateAsync("Real 1", VipProductType.RealExam, null, 100, 30);
        
        var cats = await _service.GetEnabledAsync(type: VipProductType.Category);
        Assert.AreEqual(2, cats.Count);
        
        var reals = await _service.GetEnabledAsync(type: VipProductType.RealExam);
        Assert.AreEqual(1, reals.Count);
    }

    [TestMethod]
    public async Task CourseProductsRejectKnowledgeCategoriesAndPreserveExistingMemberships()
    {
        var course = new TotalCategory { Title = "Course" };
        var chapter = new Category { Id = Guid.NewGuid(), Title = "Overview", ParentId = null };
        _dbContext.AddRange(course, chapter);
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("Wrong target", VipProductType.Category, chapter.Id, 100, 30));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("Missing target", VipProductType.Category, null, 100, 30));
        var product = await _service.CreateAsync("Course VIP", VipProductType.Category, course.Id, 100, 30);
        var start = DateTime.UtcNow.AddDays(-20);
        var end = start.AddDays(366);
        var member = new VipMembership { UserId = "student", VipProductId = product.Id, StartTime = start, EndTime = end };
        _dbContext.VipMemberships.Add(member);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.UpdateAsync(product.Id, "Invalid edit", VipProductType.Category, chapter.Id, 200, 90, false));
        await _service.UpdateAsync(product.Id, "Renamed VIP", VipProductType.Category, course.Id, 200, 90, false);
        _dbContext.ChangeTracker.Clear();
        var saved = await _dbContext.VipMemberships.Include(m => m.VipProduct).SingleAsync();
        Assert.AreEqual(member.Id, saved.Id);
        Assert.AreEqual(product.Id, saved.VipProductId);
        Assert.AreEqual(start, saved.StartTime);
        Assert.AreEqual(end, saved.EndTime);
        Assert.AreEqual(course.Id, saved.VipProduct!.TotalCategoryId);
        Assert.IsTrue(saved.IsActive, "Stopping new sales must not revoke an existing membership.");
        Assert.AreEqual(0, (await _service.GetEnabledAsync(course.Id)).Count);
    }

    [TestMethod]
    public async Task InstitutionProductsRequireAnExistingSchoolAndFilterIndependently()
    {
        var first = new Institution { Title = "Wuhan" };
        var second = new Institution { Title = "Suzhou" };
        var course = new TotalCategory { Title = "Chinese history" };
        _dbContext.AddRange(first, second, course);
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("Missing school", VipProductType.Institution, null, 100, 30));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            _service.CreateAsync("Not a school", VipProductType.Institution, null, 100, 30, course.Id));
        var product = await _service.CreateAsync("Wuhan VIP", VipProductType.Institution, course.Id, 100, 30, first.Id);
        await _service.CreateAsync("Suzhou VIP", VipProductType.Institution, null, 100, 30, second.Id);
        await _service.CreateAsync("History VIP", VipProductType.Category, course.Id, 100, 30, first.Id);
        await _service.CreateAsync("Legacy all schools", VipProductType.RealExam, null, 100, 30, first.Id);
        _dbContext.ChangeTracker.Clear();
        var selected = (await _service.GetEnabledAsync(institutionId: first.Id)).Single();
        Assert.AreEqual(product.Id, selected.Id);
        Assert.AreEqual("Wuhan", selected.Institution!.Title);
        Assert.IsNull(selected.TotalCategoryId);
        Assert.AreEqual(2, (await _service.GetEnabledAsync(type: VipProductType.Institution)).Count);
        Assert.AreEqual(0, (await _service.GetEnabledAsync(course.Id, institutionId: first.Id)).Count);
        Assert.IsTrue((await _service.GetAllAsync()).Where(p => p.Type != VipProductType.Institution).All(p => p.InstitutionId == null));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PurchasedScopeCannotBeRetargetedOrDeleted(bool pendingOrder)
    {
        var school = new Institution { Title = "Purchased school" };
        var other = new Institution { Title = "Other school" };
        _dbContext.AddRange(school, other);
        await _dbContext.SaveChangesAsync();
        var product = await _service.CreateAsync("School VIP", VipProductType.Institution, null, 100, 30, school.Id);
        var end = DateTime.UtcNow.AddDays(17);
        if (pendingOrder)
            _dbContext.PaymentOrders.Add(new PaymentOrder { UserId = "student", VipProductId = product.Id, OutTradeNo = "Pending-school", Description = "School purchase", Status = PaymentOrderStatus.Pending });
        else
            _dbContext.VipMemberships.Add(new VipMembership { UserId = "student", VipProductId = product.Id, StartTime = end.AddDays(-30), EndTime = end });
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(product.Id, "Wrong", VipProductType.Institution, null, 200, 60, false, other.Id));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(product.Id, "Wrong", VipProductType.RealExam, null, 200, 60, false));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => _service.DeleteAsync(product.Id));
        await _service.UpdateAsync(product.Id, "Stopped sales", VipProductType.Institution, null, 200, 60, false, school.Id);
        _dbContext.ChangeTracker.Clear();
        var saved = (await _service.GetByIdAsync(product.Id))!;
        Assert.AreEqual(school.Id, saved.InstitutionId);
        Assert.IsFalse(saved.IsEnabled);
        if (pendingOrder)
        {
            var order = await _dbContext.PaymentOrders.SingleAsync();
            Assert.AreEqual(product.Id, order.VipProductId);
            Assert.AreEqual(PaymentOrderStatus.Pending, order.Status);
        }
        else
        {
            var membership = await _dbContext.VipMemberships.SingleAsync();
            Assert.AreEqual(end, membership.EndTime);
            Assert.IsTrue(membership.IsActive);
        }
    }

    [TestMethod]
    public async Task LegacyAllSchoolMembershipCannotBeNarrowedToOneSchool()
    {
        var school = new Institution { Title = "School" };
        _dbContext.Institutions.Add(school);
        await _dbContext.SaveChangesAsync();
        var product = await _service.CreateAsync("Legacy", VipProductType.RealExam, null, 100, 365);
        var start = DateTime.UtcNow.AddDays(-5);
        var end = start.AddDays(365);
        _dbContext.VipMemberships.Add(new VipMembership { UserId = "old-student", VipProductId = product.Id, StartTime = start, EndTime = end });
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(product.Id, "School", VipProductType.Institution, null, 100, 365, true, school.Id));
        _dbContext.ChangeTracker.Clear();
        var member = await _dbContext.VipMemberships.Include(m => m.VipProduct).SingleAsync();
        Assert.AreEqual(VipProductType.RealExam, member.VipProduct!.Type);
        Assert.IsNull(member.VipProduct.InstitutionId);
        Assert.AreEqual(start, member.StartTime);
        Assert.AreEqual(end, member.EndTime);
    }

}
