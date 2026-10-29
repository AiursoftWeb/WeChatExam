using Aiursoft.WeChatExam.MySql;
using Aiursoft.WeChatExam.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Aiursoft.WeChatExam.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.WeChatExam.Tests.IntegrationTests;

/// <summary>
/// This test class ensures that the Entity Framework migrations are up-to-date for all supported database providers.
/// If you change the database model (entities), you must create a new migration for both SQLite and MySQL.
/// </summary>
[TestClass]
public class MigrationTests
{
    [TestMethod]
    public async Task MovingVipProductsPreservesPurchasedEntitlementsAndRemovesTheOldForeignKey()
    {
        await using var context = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261026191817_RequireQuestionPeriod");
        var course = new TotalCategory { Title = "History" };
        var legacy = new Category { Id = course.Id, Title = "Old course title", ParentId = null };
        var student = new User { UserName = "student", DisplayName = "Student" };
        context.AddRange(course, legacy, student);
        await context.SaveChangesAsync();
        var productId = Guid.NewGuid();
        var realId = Guid.NewGuid();
        var created = DateTime.UtcNow.AddYears(-1);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO VipProducts (Id, Name, Type, CategoryId, PriceInFen, DurationDays, IsEnabled, CreationTime)
            VALUES ({productId}, 'Retained course VIP', 0, {course.Id}, 19800, 366, 0, {created}),
                   ({realId}, 'All real exams', 1, NULL, 29800, 365, 1, {created})
            """);
        var order = new PaymentOrder
        {
            OutTradeNo = "existing-paid-order", UserId = student.Id, VipProductId = productId,
            Description = "Paid before migration", AmountInFen = 19800, Status = PaymentOrderStatus.Paid
        };
        var member = new VipMembership
        {
            UserId = student.Id, VipProductId = productId, StartTime = created, EndTime = created.AddYears(2),
            LastPaymentOrder = order
        };
        var realMember = new VipMembership
        {
            UserId = student.Id, VipProductId = realId, StartTime = created, EndTime = created.AddYears(2)
        };
        context.AddRange(order, member, realMember);
        await context.SaveChangesAsync();
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();

        var saved = await context.VipMemberships.Include(m => m.VipProduct)
            .ThenInclude(p => p!.TotalCategory).Include(m => m.LastPaymentOrder).SingleAsync(m => m.Id == member.Id);
        Assert.AreEqual(productId, saved.VipProductId);
        Assert.AreEqual(member.StartTime, saved.StartTime);
        Assert.AreEqual(member.EndTime, saved.EndTime);
        Assert.AreEqual(order.Id, saved.LastPaymentOrderId);
        Assert.AreEqual(PaymentOrderStatus.Paid, saved.LastPaymentOrder!.Status);
        Assert.AreEqual(19800, saved.LastPaymentOrder.AmountInFen);
        Assert.AreEqual(course.Id, saved.VipProduct!.TotalCategoryId);
        Assert.AreEqual("History", saved.VipProduct.TotalCategory!.Title);
        Assert.AreEqual(created, saved.VipProduct.CreationTime);
        Assert.AreEqual(366, saved.VipProduct.DurationDays);
        Assert.IsFalse(saved.VipProduct.IsEnabled);
        var real = await context.VipMemberships.Include(m => m.VipProduct).SingleAsync(m => m.Id == realMember.Id);
        Assert.IsNull(real.VipProduct!.TotalCategoryId);
        Assert.AreEqual(VipProductType.RealExam, real.VipProduct.Type);
        Assert.AreEqual(realMember.EndTime, real.EndTime);

        // A knowledge-directory entry no longer owns the purchased course membership.
        context.Categories.Remove(await context.Categories.SingleAsync());
        await context.SaveChangesAsync();
        Assert.AreEqual(2, await context.VipMemberships.CountAsync());
        context.ChangeTracker.Clear();
        await Assert.ThrowsExactlyAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM TotalCategories WHERE Id = {course.Id}"));
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('VipProducts') WHERE name = 'CategoryId'";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync());
    }

    [TestMethod]
    public async Task RemovingKnowledgeHierarchyPreservesContentAndCategoryOrder()
    {
        var options = new DbContextOptionsBuilder<SqliteContext>().UseSqlite("DataSource=:memory:").Options;
        await using var context = new SqliteContext(options);
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261026090703_AddQuestionPeriodAssignment");
        var course = new Category { Id = Guid.NewGuid(), Title = "History", ParentId = null };
        var chapter = new Category { Id = Guid.NewGuid(), Title = "Early music", ParentId = course.Id };
        var point = new KnowledgePoint { Id = Guid.NewGuid(), Title = "Overview", Content = "Preserved lesson", AudioUrl = "/lesson.mp3" };
        var question = new Question { QuestionCategory = QuestionPeriodAssignmentTests.TestPeriod(), Id = Guid.NewGuid(), Content = "Preserved exercise" };
        context.AddRange(new TotalCategory { Id = course.Id, Title = course.Title }, course, chapter, point, question,
            new KnowledgePointQuestion { KnowledgePointId = point.Id, QuestionId = question.Id });
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CategoryKnowledgePoints (Id, CategoryId, KnowledgePointId, OrderIndex) VALUES ({Guid.NewGuid()}, {chapter.Id}, {point.Id}, 7)");
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        var saved = await context.KnowledgePoints.Include(k => k.KnowledgeCategoryKnowledgePoints)
            .Include(k => k.KnowledgePointQuestions).SingleAsync();
        Assert.AreEqual(point.Id, saved.Id);
        Assert.AreEqual("Preserved lesson", saved.Content);
        Assert.AreEqual("/lesson.mp3", saved.AudioUrl);
        Assert.AreEqual(7, saved.KnowledgeCategoryKnowledgePoints.Single().OrderIndex);
        Assert.AreEqual(chapter.Id, saved.KnowledgeCategoryKnowledgePoints.Single().KnowledgeCategoryId);
        Assert.AreEqual(question.Id, saved.KnowledgePointQuestions.Single().QuestionId);
        Assert.AreEqual(course.Id, (await context.Categories.SingleAsync(c => c.Id == chapter.Id)).ParentId);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM pragma_table_info('KnowledgePoints') WHERE name = 'ParentId'";
        Assert.AreEqual(0L, await command.ExecuteScalarAsync());
    }


    [TestMethod]
    public async Task IndependentKnowledgeTreePreservesDirectoriesAndLinksAcrossRollback()
    {
        await using var context = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:").Options);
        await context.Database.OpenConnectionAsync();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20261027084515_MoveVipProductsToTotalCategories");
        var course = new TotalCategory { Title = "Chinese history" };
        var other = new TotalCategory { Title = "Western history" };
        var root = new Category { Id = course.Id, Title = "Chinese history", ParentId = null };
        var chapter = new Category { Id = Guid.NewGuid(), Title = "Early music", ParentId = root.Id, IsFree = true, OrderIndex = 8 };
        var child = new Category { Id = Guid.NewGuid(), Title = "Overview", ParentId = chapter.Id, IsFree = false, OrderIndex = 2 };
        var lesson = new KnowledgePoint { Title = "Lesson", Content = "Original content", AudioUrl = "/audio.mp3" };
        context.AddRange(course, other, root, chapter, child, lesson);
        await context.SaveChangesAsync();
        var linkId = Guid.NewGuid();
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO CategoryKnowledgePoints (Id, CategoryId, KnowledgePointId, OrderIndex) VALUES ({linkId}, {child.Id}, {lesson.Id}, 9)");
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.AreEqual(2, await context.KnowledgeCategories.CountAsync());
        var migrated = await context.KnowledgeCategories.SingleAsync(c => c.Id == chapter.Id);
        Assert.IsNull(migrated.ParentKnowledgeCategoryId);
        Assert.AreEqual(course.Id, migrated.TotalCategoryId);
        Assert.IsTrue(migrated.IsFree);
        Assert.AreEqual(8, migrated.OrderIndex);
        Assert.AreEqual(chapter.CreationTime, migrated.CreationTime);
        Assert.AreEqual(chapter.Id, (await context.KnowledgeCategories.SingleAsync(c => c.Id == child.Id)).ParentKnowledgeCategoryId);
        var link = await context.KnowledgeCategoryKnowledgePoints.SingleAsync();
        Assert.AreEqual(linkId, link.Id);
        Assert.AreEqual(9, link.OrderIndex);
        await Assert.ThrowsExactlyAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync($"UPDATE KnowledgeCategories SET TotalCategoryId = {other.Id} WHERE Id = {child.Id}"));
        var added = new KnowledgeCategory { Title = "New chapter", TotalCategoryId = course.Id, ParentKnowledgeCategoryId = chapter.Id };
        context.Add(added);
        migrated.Title = "Updated early music";
        await context.SaveChangesAsync();
        await migrator.MigrateAsync("20261027084515_MoveVipProductsToTotalCategories");
        context.ChangeTracker.Clear();
        Assert.AreEqual(chapter.Id, (await context.Categories.SingleAsync(c => c.Id == added.Id)).ParentId);
        Assert.AreEqual("Updated early music", (await context.Categories.SingleAsync(c => c.Id == chapter.Id)).Title);
        await migrator.MigrateAsync();
        context.ChangeTracker.Clear();
        Assert.AreEqual(3, await context.KnowledgeCategories.CountAsync());
        Assert.AreEqual(linkId, (await context.KnowledgeCategoryKnowledgePoints.SingleAsync()).Id);
        Assert.AreEqual("Original content", (await context.KnowledgePoints.SingleAsync()).Content);
        Assert.AreEqual("/audio.mp3", (await context.KnowledgePoints.SingleAsync()).AudioUrl);
    }

    [TestMethod]
    public async Task RemovingTagPricingPreservesInternalOrganizationAndLinks()
    {
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:;Foreign Keys=True").Options);
        await db.Database.OpenConnectionAsync();
        var migrator = db.GetService<IMigrator>();
        const string previous = "20261028112940_AddInstitutionVipProducts";
        await migrator.MigrateAsync(previous);
        var taxonomy = new Taxonomy { Name = "Internal sources" };
        var first = new Tag { DisplayName = "Previously free", NormalizedName = "PREVIOUSLY FREE", OrderIndex = 7, Taxonomy = taxonomy };
        var second = new Tag { DisplayName = "Previously paid", NormalizedName = "PREVIOUSLY PAID", OrderIndex = 9 };
        var period = QuestionPeriodAssignmentTests.TestPeriod();
        var question = new Question { Content = "Preserved question", StandardAnswer = "Original answer", QuestionCategory = period };
        var paper = new Paper { Title = "Retained legacy paper", IsFree = false };
        db.AddRange(first, second, question, paper);
        await db.SaveChangesAsync();
        db.AddRange(new QuestionTag { QuestionId = question.Id, TagId = first.Id },
            new QuestionTag { QuestionId = question.Id, TagId = second.Id },
            new PaperTag { PaperId = paper.Id, TagId = first.Id });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Tags SET IsFree = 1 WHERE Id = {first.Id}");

        foreach (var target in new[] { null, previous, null })
        {
            await migrator.MigrateAsync(target);
            db.ChangeTracker.Clear();
            var tags = await db.Tags.OrderBy(t => t.OrderIndex).ToListAsync();
            CollectionAssert.AreEqual(new[] { first.Id, second.Id }, tags.Select(t => t.Id).ToArray());
            CollectionAssert.AreEqual(new[] { "Previously free", "Previously paid" }, tags.Select(t => t.DisplayName).ToArray());
            CollectionAssert.AreEqual(new[] { first.NormalizedName, second.NormalizedName }, tags.Select(t => t.NormalizedName).ToArray());
            CollectionAssert.AreEqual(new[] { 7, 9 }, tags.Select(t => t.OrderIndex).ToArray());
            Assert.AreEqual(taxonomy.Id, tags[0].TaxonomyId);
            Assert.IsNull(tags[1].TaxonomyId);
            Assert.AreEqual(2, await db.QuestionTags.CountAsync(t => t.QuestionId == question.Id));
            Assert.AreEqual(first.Id, (await db.PaperTags.SingleAsync(t => t.PaperId == paper.Id)).TagId);
            Assert.AreEqual("Original answer", (await db.Questions.SingleAsync()).StandardAnswer);
            Assert.IsFalse((await db.QuestionCategories.SingleAsync()).IsFree);
            Assert.IsFalse((await db.Papers.SingleAsync()).IsFree);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT count(*) FROM pragma_table_info('Tags') WHERE name = 'IsFree'";
            Assert.AreEqual(target == null ? 0L : 1L, await command.ExecuteScalarAsync());
            command.CommandText = "SELECT count(*) FROM pragma_foreign_key_check";
            Assert.AreEqual(0L, await command.ExecuteScalarAsync());
        }
        // Downgrading restores the schema default, not historical pricing values.
        // Exact restoration of retired metadata requires the pre-upgrade backup.
    }

    [TestMethod]
    public void TestSqliteMigrations()
    {
        var options = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var context = new SqliteContext(options);
        var hasPendingChanges = context.Database.HasPendingModelChanges();
        Assert.IsFalse(hasPendingChanges, "There are pending model changes for Sqlite. Please run 'dotnet ef migrations add' for Sqlite.");
    }

    [TestMethod]
    public void TestMySqlMigrations()
    {
        var options = new DbContextOptionsBuilder<MySqlContext>()
            .UseMySql("Server=localhost;Database=test;Uid=root;Pwd=password;", new MySqlServerVersion(new Version(8, 0, 31)))
            .Options;
        using var context = new MySqlContext(options);
        var hasPendingChanges = context.Database.HasPendingModelChanges();
        Assert.IsFalse(hasPendingChanges, "There are pending model changes for MySql. Please run 'dotnet ef migrations add' for MySql.");
    }
}
